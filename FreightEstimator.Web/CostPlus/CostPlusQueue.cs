using System;
using System.Data;
using System.Data.SqlClient;
using AuthenticationServer.Models;

namespace AuthenticationServer.CostPlus
{
    /// <summary>What the CostPlus request table says about the poller's queue, for the health check (#35).</summary>
    internal static class CostPlusQueue
    {
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
