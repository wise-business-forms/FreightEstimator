using System;
using System.Collections.Generic;
using System.Threading;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// The poller's heartbeat and the two measures a stall shows up in (#35, ADR 0005): how long the oldest unclaimed request
    /// has been waiting, and how long since a poll last succeeded. Turns unhealthy past the thresholds. A poll that finds
    /// nothing to answer still counts as successful: the poller reached the table and looked.
    /// </summary>
    internal class CostPlusPollerHealth
    {
        public static readonly TimeSpan MaxOldestUnclaimedAge = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxTimeSinceSuccessfulPoll = TimeSpan.FromSeconds(60);

        private readonly Func<DateTime> _utcNow;
        private readonly DateTime _startedUtc;
        private long _lastSuccessTicks;   // 0: none yet
        private long _lastAlertTicks, _lastProbeTicks;   // 0: none yet
        // _droppedEvents is reserved for Poller Events (#51): nothing records a Poller Event yet, so it stays 0 until that ticket.
        private long _idlePolls, _requestsHandled, _droppedEvents;

        public CostPlusPollerHealth(Func<DateTime> utcNow = null)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _startedUtc = _utcNow();
        }

        public void RecordSuccessfulPoll() => Interlocked.Exchange(ref _lastSuccessTicks, _utcNow().Ticks);

        /// <summary>A poll that found nothing to answer (counted, never an event).</summary>
        public void RecordIdlePoll() => Interlocked.Increment(ref _idlePolls);

        /// <summary>Requests this instance answered (or, in the Shadow stage, compared).</summary>
        public void RecordRequestsHandled(int count) => Interlocked.Add(ref _requestsHandled, count);

        /// <summary>The stall monitor sent an email.</summary>
        public void RecordAlertSent() => Interlocked.Exchange(ref _lastAlertTicks, _utcNow().Ticks);

        /// <summary>The probe put a request in the request table.</summary>
        public void RecordProbeInserted() => Interlocked.Exchange(ref _lastProbeTicks, _utcNow().Ticks);

        /// <summary>What this instance reports in its heartbeat (#50): the counters, and how long ago things last happened.</summary>
        public PollerBeat ToBeat(string instanceId, string machine, string mode)
        {
            DateTime now = _utcNow();
            return new PollerBeat
            {
                InstanceId = instanceId, Machine = machine, Mode = mode,
                Uptime = now - _startedUtc,
                SinceLastSuccessfulPoll = Since(now, Interlocked.Read(ref _lastSuccessTicks)),
                LastAlertSentAgo = Since(now, Interlocked.Read(ref _lastAlertTicks)),
                LastProbeInsertedAgo = Since(now, Interlocked.Read(ref _lastProbeTicks)),
                IdlePolls = Interlocked.Read(ref _idlePolls),
                RequestsHandled = Interlocked.Read(ref _requestsHandled),
                DroppedEvents = Interlocked.Read(ref _droppedEvents)
            };
        }

        private static TimeSpan? Since(DateTime now, long ticks) => ticks == 0 ? (TimeSpan?)null : now - new DateTime(ticks, DateTimeKind.Utc);

        /// <param name="oldestUnclaimedAge">Null when no request is waiting.</param>
        public Report Evaluate(TimeSpan? oldestUnclaimedAge)
        {
            DateTime now = _utcNow();
            long ticks = Interlocked.Read(ref _lastSuccessTicks);
            DateTime? last = ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc);
            // Before the first poll the clock runs from startup, so a poller that never gets going is still caught.
            TimeSpan sincePoll = now - (last ?? _startedUtc);

            var problems = new List<string>();
            if (oldestUnclaimedAge > MaxOldestUnclaimedAge)
                problems.Add("The oldest unclaimed request has waited " + (int)oldestUnclaimedAge.Value.TotalSeconds + " s (limit " + (int)MaxOldestUnclaimedAge.TotalSeconds + " s).");
            if (sincePoll > MaxTimeSinceSuccessfulPoll)
                problems.Add("No poll has succeeded for " + (int)sincePoll.TotalSeconds + " s (limit " + (int)MaxTimeSinceSuccessfulPoll.TotalSeconds + " s).");

            return new Report
            {
                Healthy = problems.Count == 0,
                OldestUnclaimedRequestAgeSeconds = oldestUnclaimedAge?.TotalSeconds,
                LastSuccessfulPollUtc = last,
                SecondsSinceLastSuccessfulPoll = sincePoll.TotalSeconds,
                Problems = problems
            };
        }

        public class Report
        {
            public bool Healthy { get; set; }
            public double? OldestUnclaimedRequestAgeSeconds { get; set; }
            public DateTime? LastSuccessfulPollUtc { get; set; }
            public double SecondsSinceLastSuccessfulPoll { get; set; }
            public List<string> Problems { get; set; }
        }
    }
}
