using System;
using System.Data;
using System.Data.SqlClient;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// Records a Quote the API produced: its Quote ID, the Consumer that asked, and the Reference Number it
    /// supplied (docs/adr/0001, 0002). Per-service Calculated Rates attach to the Quote by QuoteId through
    /// dbo.QuoteServiceCalculation. Additive: nothing here touches the Plant page's logging.
    /// </summary>
    public static class QuoteLog
    {
        public static void Record(Guid quoteId, int consumerId, string referenceNumber, string plantCode, string requestJson)
        {
            using (SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection))
            using (SqlCommand cmd = new SqlCommand("LogQuote", conn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@QuoteId", SqlDbType.UniqueIdentifier).Value = quoteId;
                cmd.Parameters.Add("@ConsumerId", SqlDbType.Int).Value = consumerId;
                cmd.Parameters.Add("@ReferenceNumber", SqlDbType.VarChar, 100).Value = referenceNumber;
                cmd.Parameters.Add("@PlantCode", SqlDbType.VarChar, 10).Value = plantCode;
                cmd.Parameters.Add("@RequestJson", SqlDbType.NVarChar, -1).Value = requestJson;
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
