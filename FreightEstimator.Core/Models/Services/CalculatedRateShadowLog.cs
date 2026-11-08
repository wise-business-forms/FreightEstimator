using System;
using System.Data;
using System.Data.SqlClient;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// Writes shadow-mode comparisons to a new, dedicated, throwaway table
    /// (Database/002_create_calculated_rate_shadow_log.sql) - deliberately kept separate from
    /// QuoteServiceCalculation so the "official" persisted Calculated Rate and the shadow
    /// candidate value are never confused. Best-effort, matching
    /// Log.LogQuoteServiceCalculation: a shadow-log failure must never break a live quote
    /// request. See docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md.
    /// </summary>
    public static class CalculatedRateShadowLog
    {
        public static void Record(CalculatedRateShadowComparison comparison)
        {
            if (comparison == null || Log.WritesSuppressed) return;

            try
            {
                using (var connection = new SqlConnection(Configuration.UpsRateSqlConnection))
                {
                    connection.Open();

                    using (var command = new SqlCommand("LogCalculatedRateShadowComparison", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;

                        command.Parameters.Add(new SqlParameter("@QuoteId", SqlDbType.UniqueIdentifier) { Value = comparison.QuoteId });
                        command.Parameters.Add(new SqlParameter("@CallSite", SqlDbType.VarChar, 30) { Value = (object)comparison.CallSite ?? DBNull.Value });
                        command.Parameters.Add(new SqlParameter("@PlantCode", SqlDbType.VarChar, 10) { Value = (object)comparison.PlantCode ?? DBNull.Value });
                        command.Parameters.Add(new SqlParameter("@ServiceCode", SqlDbType.VarChar, 20) { Value = (object)comparison.ServiceCode ?? DBNull.Value });
                        command.Parameters.Add(new SqlParameter("@ServiceName", SqlDbType.VarChar, 100) { Value = (object)comparison.ServiceName ?? DBNull.Value });
                        command.Parameters.Add(new SqlParameter("@ExistingCalculatedRate", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = comparison.ExistingCalculatedRate });
                        command.Parameters.Add(new SqlParameter("@NewCalculatedRate", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = comparison.NewCalculatedRate });

                        command.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                // Best-effort only - see summary above.
            }
        }
    }
}
