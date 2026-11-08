using System;
using System.Data;
using System.Data.SqlClient;
using AuthenticationServer.Models.Services;
using Newtonsoft.Json;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// The observe-only shadow run (#36, ADR 0005). The console app still answers every request. For each UPS-only request it has
    /// already answered, the poller works out its own result the same way as a live cycle, and stores both in
    /// dbo.CostPlusShadowResult so every divergence can be reviewed. It never claims a row and never writes to the request table.
    /// UPS is asked, but nothing else is logged: no Quote, no Reference Number, no Requests_Rate or Calculated Rate rows, so
    /// reporting sees nothing of it.
    /// </summary>
    internal partial class CostPlusPollCycle
    {
        /// <summary>
        /// Only requests this recent are compared, so switching the shadow run on does not replay the table's history through UPS.
        /// </summary>
        private const int ShadowWindowHours = 24;

        /// <summary>
        /// Which requests the shadow run compares: UPS-only, answered (processed and rated) by the console app, and not a probe. Needs a
        /// @probe parameter. The Poller Shadow Run report uses the same text for its not-compared count, so "eligible" means one thing.
        /// </summary>
        internal const string ShadowEligibleWhere =
            "f.RequestUPS = 1 AND ISNULL(f.RequestLTL, 0) = 0 " +
            "  AND f.DateProcessed IS NOT NULL AND f.DateRated IS NOT NULL " +
            "  AND f.LoginId NOT LIKE @probe + '%' ";

        /// <summary>The request has no Shadow Comparison yet.</summary>
        internal const string NotComparedWhere =
            "NOT EXISTS (SELECT 1 FROM dbo.CostPlusShadowResult s WHERE s.LoginId = f.LoginId AND s.QtyNumber = f.QtyNumber AND s.DateRequested = f.DateRequested) ";

        /// <summary>Compares one more answered request. Returns how many it compared (0 or 1).</summary>
        public int RunShadowOnce()
        {
            using (SqlConnection conn = new SqlConnection(AuthenticationServer.Models.Configuration.FreightEstimatorSqlConnection))
            {
                conn.Open();

                Request request = FindNextToCompare(conn);
                if (request == null) return 0;

                CostPlusShadowComparison.Result console = ReadConsoleResult(conn, request);
                CostPlusShadowComparison.Result poller = ComputePollerResult(conn, request);

                return Store(conn, request, console, poller) ? 1 : 0;
            }
        }

        /// <summary>The oldest recent UPS-only request the console app has answered and the shadow run has not compared yet. Probes are left out.</summary>
        private static Request FindNextToCompare(SqlConnection conn)
        {
            using (SqlCommand cmd = new SqlCommand(
                "SELECT TOP 1 " + RequestColumns +
                "FROM dbo.FreightRequests f " +
                "WHERE " + ShadowEligibleWhere +
                "  AND f.DateRequested >= DATEADD(HOUR, -@hours, GETDATE()) " +
                "  AND " + NotComparedWhere +
                "ORDER BY f.DateRequested, f.LoginId, f.QtyNumber", conn))
            {
                cmd.Parameters.Add("@hours", SqlDbType.Int).Value = ShadowWindowHours;
                cmd.Parameters.Add("@probe", SqlDbType.VarChar, 50).Value = CostPlusProbe.LoginIdPrefix;
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    return r.Read() ? ReadRequest(r) : null;
                }
            }
        }

        /// <summary>What the console app wrote to the request's row.</summary>
        private static CostPlusShadowComparison.Result ReadConsoleResult(SqlConnection conn, Request request)
        {
            var result = new CostPlusShadowComparison.Result();
            using (SqlCommand cmd = new SqlCommand("SELECT * FROM dbo.FreightRequests WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d", conn))
            {
                AddKey(cmd, request);
                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return result;
                    result.ErrorCode = r["ErrorCode01"] as string;
                    result.ErrorDescription = r["ErrorDesc01"] as string;
                    result.ClassifiedResidential = r["ClassifiedResidential"] as bool?;
                    result.SubstitutedZip = r["SubstitutedZip"] as string;
                    for (int i = 1; i <= CostPlusResultShape.MaxSlots; i++)
                    {
                        string n = i.ToString("00");
                        if (r["Service" + n] is DBNull && r["Rate" + n] is DBNull) continue;
                        result.Lines.Add(new CostPlusShadowComparison.Line
                        {
                            Service = r["Service" + n] as string,
                            Rate = r["Rate" + n] is DBNull ? 0m : Convert.ToDecimal(r["Rate" + n]),
                            Note = r["Note" + n] as string
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>The result a live cycle would have written, without writing it. A failure to work one out is itself a result to compare.</summary>
        private CostPlusShadowComparison.Result ComputePollerResult(SqlConnection conn, Request request)
        {
            Outcome outcome;
            try
            {
                using (AuthenticationServer.Models.Services.Log.SuppressWrites())
                {
                    outcome = Compute(conn, request, shadow: true);
                }
            }
            catch (Exception ex)
            {
                Log(Microsoft.Extensions.Logging.LogLevel.Warning, request, "CostPlus shadow run could not work out {LoginId}/{QtyNumber}: {Message}", ex.Message);
                return new CostPlusShadowComparison.Result { ErrorCode = General, ErrorDescription = "Shadow run failed: " + ex.Message };
            }

            var result = new CostPlusShadowComparison.Result { ErrorCode = outcome.ErrorCode, ErrorDescription = outcome.ErrorDescription };
            foreach (CostPlusResultShape.Slot slot in outcome.Shaped.Slots)
                result.Lines.Add(new CostPlusShadowComparison.Line { Service = slot.ServiceName, Rate = slot.Rate, Note = slot.Note.ToString() });
            if (outcome.Shaped.Slots.Count > 0)   // as WriteResult: these two are only written alongside rates
            {
                result.ClassifiedResidential = outcome.Shaped.ClassifiedResidential;
                result.SubstitutedZip = outcome.Shaped.SubstitutedZip;
            }
            return result;
        }

        /// <summary>Stores both results and the differences. A request already compared (another instance got there first) is skipped.</summary>
        private static bool Store(SqlConnection conn, Request request, CostPlusShadowComparison.Result console, CostPlusShadowComparison.Result poller)
        {
            var differences = CostPlusShadowComparison.Differences(console, poller);
            using (SqlCommand cmd = new SqlCommand(
                "INSERT dbo.CostPlusShadowResult (LoginId, QtyNumber, DateRequested, ConsoleResult, PollerResult, Diverged, Differences) " +
                "VALUES (@l, @q, @d, @console, @poller, @diverged, @differences)", conn))
            {
                AddKey(cmd, request);
                cmd.Parameters.Add("@console", SqlDbType.NVarChar, -1).Value = JsonConvert.SerializeObject(console);
                cmd.Parameters.Add("@poller", SqlDbType.NVarChar, -1).Value = JsonConvert.SerializeObject(poller);
                cmd.Parameters.Add("@diverged", SqlDbType.Bit).Value = differences.Count > 0;
                cmd.Parameters.Add("@differences", SqlDbType.NVarChar, -1).Value = differences.Count == 0 ? (object)DBNull.Value : string.Join(Environment.NewLine, differences);
                try
                {
                    cmd.ExecuteNonQuery();
                    return true;
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    return false;
                }
            }
        }
    }
}
