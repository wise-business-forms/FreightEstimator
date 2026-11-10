using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using AuthenticationServer.Models;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// One CostPlus request as the Poller Monitor shows it (#49): who asked, where from and to, which carriers, how long ago.
    /// Deliberately no street address and no result: the monitor shows that a request is there, not what it says.
    /// </summary>
    internal class QueueRequest
    {
        public string LoginId, FromState, ToZip, ClaimedBy;
        public int QtyNumber;
        public bool Ups, Ltl, IsProbe;
        /// <summary>Since the request was made, on the database's clock.</summary>
        public double AgeSeconds;
    }

    /// <summary>The newest probe request in the table: when it was made and whether the poller has answered it.</summary>
    internal class ProbeStatus
    {
        public double SentAgeSeconds;
        public bool Answered;
    }

    /// <summary>The queue at one moment (#49): how many requests wait, the oldest ten, what is in flight, and the latest probe.</summary>
    internal class QueueSnapshot
    {
        public int Waiting;
        public List<QueueRequest> Oldest = new List<QueueRequest>();
        public List<QueueRequest> InFlight = new List<QueueRequest>();
        public ProbeStatus LatestProbe;

        /// <summary>How long the oldest request waiting for the poller has waited; null if none is.</summary>
        public TimeSpan? OldestAge => Oldest.Count == 0 ? (TimeSpan?)null : TimeSpan.FromSeconds(Oldest[0].AgeSeconds);
    }

    /// <summary>What the CostPlus request table says about the poller's queue, for the health check (#35) and the Poller Monitor (#49). Read-only.</summary>
    internal static class CostPlusQueue
    {
        private const int OldestListed = 10;
        private const int InFlightListed = 50;
        private const string RequestColumns = "f.LoginId, f.QtyNumber, f.FromState, f.ToZip, f.RequestUPS, f.RequestLTL, DATEDIFF_BIG(MILLISECOND, f.DateRequested, GETDATE())";

        /// <summary>
        /// The queue as the poll cycle sees it: "waiting" is <see cref="CostPlusPollCycle.WaitingWhere"/> itself, so the monitor,
        /// the health check and the poller can never disagree about what is waiting. In flight is a live claim not yet completed
        /// on a request not yet answered. Never writes.
        /// </summary>
        public static QueueSnapshot Snapshot()
        {
            var snapshot = new QueueSnapshot();
            using (var conn = new SqlConnection(Configuration.FreightEstimatorSqlConnection))
            {
                conn.Open();

                using (SqlCommand cmd = Command(conn, "SELECT COUNT(*) FROM dbo.FreightRequests f WHERE " + CostPlusPollCycle.WaitingWhere))
                    snapshot.Waiting = (int)cmd.ExecuteScalar();

                using (SqlCommand cmd = Command(conn,
                    "SELECT TOP (" + OldestListed + ") " + RequestColumns + ", CAST(NULL AS VARCHAR(100)) FROM dbo.FreightRequests f WHERE " + CostPlusPollCycle.WaitingWhere +
                    "ORDER BY f.DateRequested, f.LoginId, f.QtyNumber"))
                    snapshot.Oldest = ReadRequests(cmd);

                // In flight is the other side of the lease test in WaitingWhere (a claim not completed and not past its lease): change them together.
                using (SqlCommand cmd = Command(conn,
                    "SELECT TOP (" + InFlightListed + ") " + RequestColumns + ", c.ClaimedBy FROM dbo.FreightRequests f " +
                    "JOIN dbo.CostPlusRequestClaim c ON c.LoginId = f.LoginId AND c.QtyNumber = f.QtyNumber AND c.DateRequested = f.DateRequested " +
                    "WHERE c.CompletedAt IS NULL AND c.ClaimedAt >= DATEADD(MINUTE, -@lease, SYSDATETIME()) AND (f.DateProcessed IS NULL OR f.DateRated IS NULL) " +
                    "ORDER BY c.ClaimedAt"))
                    snapshot.InFlight = ReadRequests(cmd);

                using (SqlCommand cmd = Command(conn,
                    "SELECT TOP 1 DATEDIFF_BIG(MILLISECOND, f.DateRequested, GETDATE()), CASE WHEN f.DateRated IS NOT NULL THEN 1 ELSE 0 END " +
                    "FROM dbo.FreightRequests f WHERE f.LoginId LIKE @prefix + '%' AND f.DateRequested IS NOT NULL ORDER BY f.DateRequested DESC"))
                {
                    cmd.Parameters.Add("@prefix", SqlDbType.VarChar, 50).Value = CostPlusProbe.LoginIdPrefix;
                    using (SqlDataReader r = cmd.ExecuteReader())
                        if (r.Read())
                            snapshot.LatestProbe = new ProbeStatus { SentAgeSeconds = Math.Max(0, r.GetInt64(0)) / 1000.0, Answered = r.GetInt32(1) == 1 };
                }
            }
            return snapshot;
        }

        private static SqlCommand Command(SqlConnection conn, string sql)
        {
            var cmd = new SqlCommand(sql, conn) { CommandTimeout = 5 };
            cmd.Parameters.Add("@lease", SqlDbType.Int).Value = CostPlusPollCycle.LeaseMinutes;
            return cmd;
        }

        private static List<QueueRequest> ReadRequests(SqlCommand cmd)
        {
            var list = new List<QueueRequest>();
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string loginId = r.GetString(0);
                    list.Add(new QueueRequest
                    {
                        LoginId = loginId, QtyNumber = r.GetByte(1),
                        FromState = r.IsDBNull(2) ? null : r.GetString(2), ToZip = r.IsDBNull(3) ? null : r.GetString(3),
                        Ups = !r.IsDBNull(4) && r.GetBoolean(4), Ltl = !r.IsDBNull(5) && r.GetBoolean(5),
                        AgeSeconds = Math.Max(0, r.GetInt64(6)) / 1000.0,
                        ClaimedBy = r.IsDBNull(7) ? null : r.GetString(7),
                        IsProbe = CostPlusProbe.IsProbe(loginId)
                    });
                }
            }
            return list;
        }

        /// <summary>How long the oldest request waiting for the poller has waited (database clock), or null if none is.</summary>
        public static TimeSpan? OldestUnclaimedAge()
        {
            using (var conn = new SqlConnection(Configuration.FreightEstimatorSqlConnection))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    "SELECT DATEDIFF_BIG(MILLISECOND, MIN(f.DateRequested), GETDATE()) FROM dbo.FreightRequests f WHERE " + CostPlusPollCycle.WaitingWhere, conn))
                {
                    cmd.Parameters.Add("@lease", SqlDbType.Int).Value = CostPlusPollCycle.LeaseMinutes;
                    object ms = cmd.ExecuteScalar();
                    if (ms == null || ms is DBNull) return null;
                    return TimeSpan.FromMilliseconds(Math.Max(0, Convert.ToInt64(ms)));
                }
            }
        }
    }
}
