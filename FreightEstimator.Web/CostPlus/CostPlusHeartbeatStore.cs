using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using AuthenticationServer.Models;

namespace AuthenticationServer.CostPlus
{
    /// <summary>What a CostPlus Poller instance reports about itself in a heartbeat (#50), as durations up to now.</summary>
    internal class PollerBeat
    {
        public string InstanceId, Machine, Mode;
        public TimeSpan Uptime;
        /// <summary>How long ago a poll last succeeded; null if none has since the instance started.</summary>
        public TimeSpan? SinceLastSuccessfulPoll;
        public long IdlePolls, RequestsHandled, DroppedEvents;
        public TimeSpan? LastAlertSentAgo, LastProbeInsertedAgo;
    }

    /// <summary>Where the instances' heartbeats are kept, so the Poller Monitor can see every instance whichever process serves the page.</summary>
    internal interface IPollerHeartbeatStore
    {
        void Write(PollerBeat beat);

        /// <summary>The instances heard from in the last hour, with ages measured by the database's clock at the moment of reading.</summary>
        List<PollerInstanceBeat> ReadRecent();
    }

    /// <summary>
    /// Heartbeats in dbo.CostPlusPollerHeartbeat in the FreightEstimator database. Every time in the row is the database's clock
    /// (a duration is subtracted from SYSDATETIME() there), so the machines' own clocks never matter. Writes are short: a heartbeat
    /// that cannot be written in a few seconds is not worth holding anything up for.
    /// </summary>
    internal class SqlPollerHeartbeatStore : IPollerHeartbeatStore
    {
        private const int WriteTimeoutSeconds = 3;
        private static readonly TimeSpan Retention = TimeSpan.FromDays(1);
        private static readonly TimeSpan RecentFor = TimeSpan.FromHours(1);

        public void Write(PollerBeat beat)
        {
            // The connect timeout is short too, not just the command's: a database that is not answering must not hold the write for the 15 s default.
            var connectionString = new SqlConnectionStringBuilder(Configuration.FreightEstimatorSqlConnection) { ConnectTimeout = WriteTimeoutSeconds };
            using (var conn = new SqlConnection(connectionString.ConnectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    "DELETE FROM dbo.CostPlusPollerHeartbeat WHERE HeartbeatAt <DATEADD(MINUTE, -@retentionMinutes, SYSDATETIME()) AND InstanceId <> @id; " +
                    "UPDATE dbo.CostPlusPollerHeartbeat SET Machine = @machine, Mode = @mode, StartedAt = DATEADD(MILLISECOND, -@uptimeMs, SYSDATETIME()), HeartbeatAt = SYSDATETIME(), " +
                    "  LastSuccessfulPollAt = CASE WHEN @pollAgoMs IS NULL THEN NULL ELSE DATEADD(MILLISECOND, -@pollAgoMs, SYSDATETIME()) END, " +
                    "  IdlePolls = @idle, RequestsHandled = @handled, DroppedEvents = @dropped, " +
                    "  LastAlertSentAt = CASE WHEN @alertAgoMs IS NULL THEN NULL ELSE DATEADD(MILLISECOND, -@alertAgoMs, SYSDATETIME()) END, " +
                    "  LastProbeInsertedAt = CASE WHEN @probeAgoMs IS NULL THEN NULL ELSE DATEADD(MILLISECOND, -@probeAgoMs, SYSDATETIME()) END " +
                    "WHERE InstanceId = @id; " +
                    "IF @@ROWCOUNT = 0 " +
                    "INSERT dbo.CostPlusPollerHeartbeat (InstanceId, Machine, Mode, StartedAt, HeartbeatAt, LastSuccessfulPollAt, IdlePolls, RequestsHandled, DroppedEvents, LastAlertSentAt, LastProbeInsertedAt) " +
                    "VALUES (@id, @machine, @mode, DATEADD(MILLISECOND, -@uptimeMs, SYSDATETIME()), SYSDATETIME(), " +
                    "  CASE WHEN @pollAgoMs IS NULL THEN NULL ELSE DATEADD(MILLISECOND, -@pollAgoMs, SYSDATETIME()) END, @idle, @handled, @dropped, " +
                    "  CASE WHEN @alertAgoMs IS NULL THEN NULL ELSE DATEADD(MILLISECOND, -@alertAgoMs, SYSDATETIME()) END, " +
                    "  CASE WHEN @probeAgoMs IS NULL THEN NULL ELSE DATEADD(MILLISECOND, -@probeAgoMs, SYSDATETIME()) END)", conn))
                {
                    cmd.CommandTimeout = WriteTimeoutSeconds;
                    cmd.Parameters.Add("@retentionMinutes", SqlDbType.Int).Value = (int)Retention.TotalMinutes;
                    cmd.Parameters.Add("@id", SqlDbType.VarChar, 100).Value = beat.InstanceId;
                    cmd.Parameters.Add("@machine", SqlDbType.VarChar, 100).Value = beat.Machine;
                    cmd.Parameters.Add("@mode", SqlDbType.VarChar, 10).Value = beat.Mode;
                    cmd.Parameters.Add("@uptimeMs", SqlDbType.BigInt).Value = (long)beat.Uptime.TotalMilliseconds;
                    AddMilliseconds(cmd, "@pollAgoMs", beat.SinceLastSuccessfulPoll);
                    AddMilliseconds(cmd, "@alertAgoMs", beat.LastAlertSentAgo);
                    AddMilliseconds(cmd, "@probeAgoMs", beat.LastProbeInsertedAgo);
                    cmd.Parameters.Add("@idle", SqlDbType.BigInt).Value = beat.IdlePolls;
                    cmd.Parameters.Add("@handled", SqlDbType.BigInt).Value = beat.RequestsHandled;
                    cmd.Parameters.Add("@dropped", SqlDbType.BigInt).Value = beat.DroppedEvents;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<PollerInstanceBeat> ReadRecent()
        {
            var beats = new List<PollerInstanceBeat>();
            using (var conn = new SqlConnection(Configuration.FreightEstimatorSqlConnection))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    "SELECT InstanceId, Machine, Mode, " +
                    "  DATEDIFF_BIG(MILLISECOND, HeartbeatAt, SYSDATETIME()), " +
                    "  CASE WHEN LastSuccessfulPollAt IS NULL THEN NULL ELSE DATEDIFF_BIG(MILLISECOND, LastSuccessfulPollAt, SYSDATETIME()) END, " +
                    "  DATEDIFF_BIG(MILLISECOND, StartedAt, SYSDATETIME()), IdlePolls, RequestsHandled, DroppedEvents, " +
                    "  CASE WHEN LastAlertSentAt IS NULL THEN NULL ELSE DATEDIFF_BIG(MILLISECOND, LastAlertSentAt, SYSDATETIME()) END, " +
                    "  CASE WHEN LastProbeInsertedAt IS NULL THEN NULL ELSE DATEDIFF_BIG(MILLISECOND, LastProbeInsertedAt, SYSDATETIME()) END " +
                    "FROM dbo.CostPlusPollerHeartbeat WHERE HeartbeatAt >= DATEADD(MINUTE, -@recentMinutes, SYSDATETIME()) ORDER BY HeartbeatAt DESC", conn))
                {
                    cmd.Parameters.Add("@recentMinutes", SqlDbType.Int).Value = (int)RecentFor.TotalMinutes;
                    using (SqlDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            beats.Add(new PollerInstanceBeat
                            {
                                InstanceId = r.GetString(0), Machine = r.GetString(1), Mode = r.GetString(2),
                                HeartbeatAge = Age(r, 3).Value, SinceLastSuccessfulPoll = Age(r, 4), StartedAgo = Age(r, 5).Value,
                                IdlePolls = r.GetInt64(6), RequestsHandled = r.GetInt64(7), DroppedEvents = r.GetInt64(8),
                                LastAlertSentAgo = Age(r, 9), LastProbeInsertedAgo = Age(r, 10)
                            });
                        }
                    }
                }
            }
            return beats;
        }

        private static void AddMilliseconds(SqlCommand cmd, string name, TimeSpan? value) =>
            cmd.Parameters.Add(name, SqlDbType.BigInt).Value = value == null ? (object)DBNull.Value : (long)value.Value.TotalMilliseconds;

        /// <summary>An age read as milliseconds, never negative (the clock can step back a little).</summary>
        private static TimeSpan? Age(SqlDataReader r, int column) =>
            r.IsDBNull(column) ? (TimeSpan?)null : TimeSpan.FromMilliseconds(Math.Max(0, r.GetInt64(column)));
    }
}
