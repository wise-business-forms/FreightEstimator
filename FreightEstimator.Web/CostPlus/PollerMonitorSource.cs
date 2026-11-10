using System;
using System.Collections.Generic;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// What the Poller Monitor read from the database at one moment (#49, #50). A part that could not be read is null with its error
    /// set, so one failing table does not blank the rest of the page, and the page never mistakes "could not read" for "nothing there".
    /// </summary>
    internal class MonitorReading
    {
        public List<PollerInstanceBeat> Beats;
        public string BeatsError;
        public QueueSnapshot Queue;
        public string QueueError;
    }

    /// <summary>Where the Poller Monitor gets its readings. The page's tests replace it; the SQL behind it has its own tests.</summary>
    internal interface IPollerMonitorSource
    {
        MonitorReading Read();
    }

    /// <summary>Reads every instance's heartbeat and the request queue from the FreightEstimator database. Read-only.</summary>
    internal class SqlPollerMonitorSource : IPollerMonitorSource
    {
        private readonly IPollerHeartbeatStore _heartbeats;

        public SqlPollerMonitorSource(IPollerHeartbeatStore heartbeats) => _heartbeats = heartbeats;

        public MonitorReading Read()
        {
            var reading = new MonitorReading();
            try { reading.Beats = _heartbeats.ReadRecent(); }
            catch (Exception ex) { reading.BeatsError = "The poller heartbeats could not be read: " + ex.Message; }
            try { reading.Queue = CostPlusQueue.Snapshot(); }
            catch (Exception ex) { reading.QueueError = "The request table could not be read: " + ex.Message; }
            return reading;
        }
    }

    /// <summary>The same readings, kept for 2 s, so several open browsers re-fetching every 2 s read the database about once per 2 s between them.</summary>
    internal class CachedPollerMonitorSource : IPollerMonitorSource
    {
        private static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(2);
        private readonly TimedCache<MonitorReading> _cache;

        public CachedPollerMonitorSource(IPollerMonitorSource inner, Func<DateTime> utcNow = null) =>
            _cache = new TimedCache<MonitorReading>(inner.Read, KeepFor, utcNow);

        public MonitorReading Read() => _cache.Get();
    }
}
