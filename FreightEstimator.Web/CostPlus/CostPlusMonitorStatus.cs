using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// What one CostPlus Poller instance last reported (#50), as ages measured on the database's clock at the moment of
    /// reading, so the machines' own clocks never matter.
    /// </summary>
    internal class PollerInstanceBeat
    {
        public string InstanceId, Machine, Mode;
        /// <summary>How long ago the instance last wrote its heartbeat.</summary>
        public TimeSpan HeartbeatAge;
        /// <summary>How long ago a poll last succeeded; null if none has since the instance started.</summary>
        public TimeSpan? SinceLastSuccessfulPoll;
        public TimeSpan StartedAgo;
        public long IdlePolls, RequestsHandled, DroppedEvents;
        public TimeSpan? LastAlertSentAgo, LastProbeInsertedAgo;
    }

    /// <summary>
    /// The Poller Monitor's verdict on what the instances reported (#50): which are running, whether any is, and whether the
    /// poller is healthy by the same limits as the health check (<see cref="CostPlusPollerHealth"/>). Pure: ages in, verdict out.
    /// </summary>
    internal class CostPlusMonitorStatus
    {
        /// <summary>An instance writes a heartbeat every 10 s; one that has not for three intervals is taken as stopped.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

        public bool AnyRunning { get; private set; }
        public bool Healthy { get; private set; }
        public List<string> Problems { get; } = new List<string>();
        public List<InstanceStatus> Instances { get; } = new List<InstanceStatus>();
        public TimeSpan? LastAlertSentAgo { get; private set; }
        public TimeSpan? LastProbeInsertedAgo { get; private set; }

        /// <summary>Properties, not fields: this is sent to the page as JSON, which leaves fields out.</summary>
        public class InstanceStatus
        {
            public string InstanceId { get; set; }
            public string Machine { get; set; }
            public string Mode { get; set; }
            public bool Running { get; set; }
            public double HeartbeatAgeSeconds { get; set; }
            public double? LastPollAgeSeconds { get; set; }
            public double StartedAgoSeconds { get; set; }
            public long IdlePolls { get; set; }
            public long RequestsHandled { get; set; }
            public long DroppedEvents { get; set; }
        }

        /// <param name="oldestUnclaimedAge">How long the oldest request waiting for the poller has waited; null if none is.</param>
        public static CostPlusMonitorStatus Judge(IEnumerable<PollerInstanceBeat> beats, TimeSpan? oldestUnclaimedAge)
        {
            var list = beats.ToList();
            var status = new CostPlusMonitorStatus();

            var running = list.Where(b => b.HeartbeatAge <= StaleAfter).ToList();
            status.AnyRunning = running.Count > 0;
            status.Instances.AddRange(list
                .OrderByDescending(b => b.HeartbeatAge <= StaleAfter).ThenBy(b => b.HeartbeatAge)
                .Select(b => new InstanceStatus
                {
                    InstanceId = b.InstanceId, Machine = b.Machine, Mode = b.Mode, Running = b.HeartbeatAge <= StaleAfter,
                    HeartbeatAgeSeconds = b.HeartbeatAge.TotalSeconds, LastPollAgeSeconds = b.SinceLastSuccessfulPoll?.TotalSeconds,
                    StartedAgoSeconds = b.StartedAgo.TotalSeconds,
                    IdlePolls = b.IdlePolls, RequestsHandled = b.RequestsHandled, DroppedEvents = b.DroppedEvents
                }));
            status.LastAlertSentAgo = Latest(list.Select(b => b.LastAlertSentAgo));
            status.LastProbeInsertedAgo = Latest(list.Select(b => b.LastProbeInsertedAgo));

            if (!status.AnyRunning)
            {
                status.Problems.Add("No poller is running: no instance has written a heartbeat in the last " + (int)StaleAfter.TotalSeconds + " s.");
            }
            else
            {
                // Before the first poll the clock runs from startup, so an instance that never gets going is still caught.
                TimeSpan freshestPoll = running.Min(b => b.SinceLastSuccessfulPoll ?? b.StartedAgo);
                if (freshestPoll > CostPlusPollerHealth.MaxTimeSinceSuccessfulPoll)
                    status.Problems.Add("No poll has succeeded for " + (int)freshestPoll.TotalSeconds + " s (limit " + (int)CostPlusPollerHealth.MaxTimeSinceSuccessfulPoll.TotalSeconds + " s).");

                // In Shadow mode the console app owns the queue, so how long a request has waited says nothing about the poller.
                bool anyLive = running.Any(b => string.Equals(b.Mode, "Live", StringComparison.OrdinalIgnoreCase));
                if (anyLive && oldestUnclaimedAge > CostPlusPollerHealth.MaxOldestUnclaimedAge)
                    status.Problems.Add("The oldest unclaimed request has waited " + (int)oldestUnclaimedAge.Value.TotalSeconds + " s (limit " + (int)CostPlusPollerHealth.MaxOldestUnclaimedAge.TotalSeconds + " s).");
            }

            status.Healthy = status.Problems.Count == 0;
            return status;
        }

        /// <summary>The most recent of several ages (the smallest), ignoring instances that never did it; null if none did.</summary>
        private static TimeSpan? Latest(IEnumerable<TimeSpan?> ages)
        {
            var known = ages.Where(a => a != null).Select(a => a.Value).ToList();
            return known.Count == 0 ? (TimeSpan?)null : known.Min();
        }
    }
}
