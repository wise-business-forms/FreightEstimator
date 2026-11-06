using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using Newtonsoft.Json;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// One pass of the CostPlus poller (#31, ADR 0005): takes the oldest UPS-only request in the CostPlus request table
    /// (dbo.FreightRequests, in FileMaker's layout), claims it, produces a Quote through the same UPS estimation path as the
    /// Quote API, writes the answer into the request's own row, and logs the Quote against the fixed "CostPlus" Consumer.
    /// FileMaker's table layout stays in this adapter; the estimation service never sees it.
    /// </summary>
    internal class CostPlusPollCycle
    {
        public const string ConsumerName = "CostPlus";

        /// <summary>How long UPS gets to answer, so the 10 second promise to CostPlus holds (ADR 0005).</summary>
        private const int UpsTimeoutMs = 10000;

        /// <summary>
        /// The Plant a request ships from, by its from-address state: the console app's table, carried over as a knowing
        /// exception to "don't infer the Plant" until CostPlus sends a plant code (ADR 0005). A request from any other
        /// state is left alone for now (rejecting it comes with the Plant mapping ticket, #33).
        /// </summary>
        private static readonly Dictionary<string, string> PlantByState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GA"] = "ALP", ["PA"] = "BUT", ["IN"] = "FTW", ["SC"] = "PDT", ["ME"] = "POR"
        };

        private readonly string _instance = Environment.MachineName + ":" + Environment.ProcessId;

        /// <summary>Runs one cycle. Returns how many requests it answered (0 or 1).</summary>
        public int RunOnce()
        {
            using (SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection))
            {
                conn.Open();

                Request request = FindNext(conn);
                if (request == null) return 0;
                if (!TryClaim(conn, request)) return 0;

                return Answer(conn, request) ? 1 : 0;
            }
        }

        private class Request
        {
            public string LoginId;
            public byte QtyNumber;
            public DateTime DateRequested;
            public string ToAddress, ToCity, ToState, ToZip, ToCountry;
            public string FromState;
            public int NumPackages, PkgWeight, LastPkgWeight;
            public string PlantCode;
        }

        /// <summary>
        /// The oldest request that asks for UPS and not LTL (the console app keeps every LTL row), has not been answered,
        /// is not already claimed, and ships from a mapped state.
        /// </summary>
        private static Request FindNext(SqlConnection conn)
        {
            string[] states = PlantByState.Keys.ToArray();
            using (SqlCommand cmd = new SqlCommand(null, conn))
            {
                var stateParameters = new List<string>();
                for (int i = 0; i < states.Length; i++)
                {
                    cmd.Parameters.Add("@s" + i, SqlDbType.VarChar, 50).Value = states[i];
                    stateParameters.Add("@s" + i);
                }
                cmd.CommandText =
                    "SELECT TOP 1 f.LoginId, f.QtyNumber, f.DateRequested, f.ToAddress, f.ToCity, f.ToState, f.ToZip, f.ToCountry, f.FromState, " +
                    "       f.NumPackages, f.PkgWeight, f.LastPkgWeight " +
                    "FROM dbo.FreightRequests f " +
                    "WHERE f.RequestUPS = 1 AND ISNULL(f.RequestLTL, 0) = 0 " +
                    "  AND (f.DateProcessed IS NULL OR f.DateRated IS NULL) " +
                    "  AND f.DateRequested IS NOT NULL " +
                    "  AND f.FromState IN (" + string.Join(", ", stateParameters) + ") " +
                    "  AND NOT EXISTS (SELECT 1 FROM dbo.CostPlusRequestClaim c WHERE c.LoginId = f.LoginId AND c.QtyNumber = f.QtyNumber AND c.DateRequested = f.DateRequested) " +
                    "ORDER BY f.DateRequested, f.LoginId, f.QtyNumber";

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    string fromState = r.IsDBNull(8) ? null : r.GetString(8);
                    return new Request
                    {
                        LoginId = r.GetString(0),
                        QtyNumber = r.GetByte(1),
                        DateRequested = r.GetDateTime(2),
                        ToAddress = r.IsDBNull(3) ? null : r.GetString(3),
                        ToCity = r.IsDBNull(4) ? null : r.GetString(4),
                        ToState = r.IsDBNull(5) ? null : r.GetString(5),
                        ToZip = r.IsDBNull(6) ? null : r.GetString(6),
                        ToCountry = r.IsDBNull(7) ? null : r.GetString(7),
                        FromState = fromState,
                        NumPackages = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                        PkgWeight = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                        LastPkgWeight = r.IsDBNull(11) ? 0 : r.GetInt32(11),
                        PlantCode = PlantByState[fromState.Trim()]
                    };
                }
            }
        }

        /// <summary>
        /// Takes the claim. The unique key on (LoginId, QtyNumber, DateRequested) is the guard: a second cycle that
        /// reaches the same row inserts nothing and gets false.
        /// </summary>
        private bool TryClaim(SqlConnection conn, Request request)
        {
            using (SqlCommand cmd = new SqlCommand(
                "INSERT dbo.CostPlusRequestClaim (LoginId, QtyNumber, DateRequested, ClaimedBy, ClaimedAt) VALUES (@l, @q, @d, @by, SYSDATETIME())", conn))
            {
                AddKey(cmd, request);
                cmd.Parameters.Add("@by", SqlDbType.VarChar, 100).Value = _instance;
                try
                {
                    cmd.ExecuteNonQuery();
                    return true;
                }
                catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                {
                    return false;   // another cycle holds it
                }
            }
        }

        private static bool Answer(SqlConnection conn, Request request)
        {
            int consumerId = ConsumerId(conn);
            int referenceNumber = NextReferenceNumber(conn);
            Guid quoteId = Guid.NewGuid();
            QuoteLog.Record(quoteId, consumerId, referenceNumber.ToString(), request.PlantCode, JsonConvert.SerializeObject(request));

            UpsParcelEstimate estimate = new UpsEstimationService().EstimateParcel(new UpsQuoteRequest
            {
                PlantCode = request.PlantCode,
                QuoteId = quoteId,
                ReferenceNumber = referenceNumber.ToString(),
                Consumer = ConsumerName,
                UserName = ConsumerName,
                AccountNumber = "0",
                Address = request.ToAddress,
                City = request.ToCity,
                State = request.ToState,
                Zip = request.ToZip,
                Country = string.IsNullOrWhiteSpace(request.ToCountry) ? "US" : request.ToCountry,
                NumberOfPackages = request.NumPackages,
                PackageWeight = request.PkgWeight,
                LastPackageWeight = request.LastPkgWeight,
                TimeoutMs = UpsTimeoutMs
            });

            if (estimate.Status != CarrierStatus.Ok)
            {
                // Writing an error back is the error-mapping ticket's job (#33); until then the request stays claimed and unanswered.
                System.Diagnostics.Trace.TraceWarning("CostPlus request " + request.LoginId + "/" + request.QtyNumber + " was not answered: " + estimate.Message);
                return false;
            }

            List<UpsParcelServiceEstimate> cheapestFirst = estimate.Services.OrderBy(s => s.CalculatedRate.RoundedAmount).Take(15).ToList();
            WriteResult(conn, request, quoteId, cheapestFirst);
            return true;
        }

        /// <summary>Writes the answer and completes the claim together, so a retry after a crash cannot answer twice.</summary>
        private static void WriteResult(SqlConnection conn, Request request, Guid quoteId, List<UpsParcelServiceEstimate> services)
        {
            using (SqlTransaction tx = conn.BeginTransaction())
            {
                using (SqlCommand cmd = new SqlCommand(null, conn, tx))
                {
                    var assignments = new List<string> { "DateProcessed = GETDATE()", "DateRated = GETDATE()" };
                    for (int i = 0; i < services.Count; i++)
                    {
                        string n = (i + 1).ToString("00");
                        assignments.Add("Carrier" + n + " = @c" + i);
                        assignments.Add("Service" + n + " = @s" + i);
                        assignments.Add("Rate" + n + " = @r" + i);
                        cmd.Parameters.Add("@c" + i, SqlDbType.VarChar, 50).Value = ServiceCatalog.Ups;
                        cmd.Parameters.Add("@s" + i, SqlDbType.VarChar, 50).Value = services[i].ServiceName;
                        cmd.Parameters.Add("@r" + i, SqlDbType.Decimal).Value = services[i].CalculatedRate.RoundedAmount;
                    }
                    cmd.CommandText = "UPDATE dbo.FreightRequests SET " + string.Join(", ", assignments) +
                        " WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d AND DateRated IS NULL";
                    AddKey(cmd, request);
                    cmd.ExecuteNonQuery();
                }

                using (SqlCommand cmd = new SqlCommand(
                    "UPDATE dbo.CostPlusRequestClaim SET CompletedAt = SYSDATETIME(), QuoteId = @quote WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d", conn, tx))
                {
                    AddKey(cmd, request);
                    cmd.Parameters.Add("@quote", SqlDbType.UniqueIdentifier).Value = quoteId;
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        private static void AddKey(SqlCommand cmd, Request request)
        {
            cmd.Parameters.Add("@l", SqlDbType.VarChar, 50).Value = request.LoginId;
            cmd.Parameters.Add("@q", SqlDbType.TinyInt).Value = request.QtyNumber;
            cmd.Parameters.Add("@d", SqlDbType.DateTime).Value = request.DateRequested;
        }

        private static int ConsumerId(SqlConnection conn)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT ConsumerId FROM dbo.Consumer WHERE Name = @n AND IsActive = 1", conn))
            {
                cmd.Parameters.Add("@n", SqlDbType.VarChar, 100).Value = ConsumerName;
                object id = cmd.ExecuteScalar();
                if (id == null) throw new InvalidOperationException("The \"" + ConsumerName + "\" Consumer is missing or inactive in dbo.Consumer.");
                return (int)id;
            }
        }

        private static int NextReferenceNumber(SqlConnection conn)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT NEXT VALUE FOR dbo.CostPlusReferenceNumber", conn))
            {
                return (int)cmd.ExecuteScalar();
            }
        }
    }
}
