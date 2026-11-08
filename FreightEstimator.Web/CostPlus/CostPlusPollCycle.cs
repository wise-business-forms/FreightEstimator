using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// One pass of the CostPlus poller (#31, ADR 0005): takes the oldest UPS-only request in the CostPlus request table
    /// (dbo.FreightRequests, in FileMaker's layout), claims it, produces a Quote through the same UPS estimation path as the
    /// Quote API, writes the answer into the request's own row, and logs the Quote against the fixed "CostPlus" Consumer.
    /// FileMaker's table layout stays in this adapter; the estimation service never sees it.
    /// </summary>
    internal partial class CostPlusPollCycle
    {
        public const string ConsumerName = "CostPlus";

        /// <summary>The error code for anything the poller cannot answer that is not a WEIGHT or ADDRESS problem, as in the console app.</summary>
        private const string General = "GENERAL";

        /// <summary>The other two codes the console app writes (ErrorCode01).</summary>
        private const string WeightError = "WEIGHT";
        private const string AddressError = "ADDRESS";

        /// <summary>The console app refuses a shipment heavier than this, in pounds.</summary>
        private const int MaxShipmentWeightLb = 19999;

        /// <summary>How long UPS gets to answer, so the 10 second promise to CostPlus holds (ADR 0005).</summary>
        private const int UpsTimeoutMs = 10000;

        /// <summary>
        /// How long a claim is live. UPS's slowest call plus margin (ADR 0005): a claim older than this that was never
        /// completed belongs to an instance that crashed or stalled, and any instance may retake it.
        /// </summary>
        private const int ClaimLeaseMinutes = 2;   // the tests use claims 180 s old (expired) and 30 s old (live)

        /// <summary>
        /// Which requests are waiting for this poller: UPS and not LTL (the console app keeps every LTL row), not answered, and
        /// with no live claim (none, or one past its lease that was never completed). Needs an @lease parameter (minutes). The
        /// lease test here and in <see cref="RetakeExpiredClaim"/> are complements: change them together. The health check
        /// (<see cref="CostPlusQueue"/>) uses the same text, so "unclaimed" means one thing.
        /// </summary>
        internal const string WaitingWhere =
            "f.RequestUPS = 1 AND ISNULL(f.RequestLTL, 0) = 0 " +
            "  AND (f.DateProcessed IS NULL OR f.DateRated IS NULL) " +
            "  AND f.DateRequested IS NOT NULL " +
            "  AND NOT EXISTS (SELECT 1 FROM dbo.CostPlusRequestClaim c WHERE c.LoginId = f.LoginId AND c.QtyNumber = f.QtyNumber AND c.DateRequested = f.DateRequested " +
            "                  AND (c.CompletedAt IS NOT NULL OR c.ClaimedAt >= DATEADD(MINUTE, -@lease, SYSDATETIME()))) ";

        internal static int LeaseMinutes => ClaimLeaseMinutes;

        private readonly string _instance = Environment.MachineName + ":" + Environment.ProcessId;
        private readonly ILogger _logger;

        public CostPlusPollCycle(ILogger logger = null)
        {
            _logger = logger;
        }

        /// <summary>Runs one cycle. Returns how many requests it answered (0 or 1).</summary>
        public int RunOnce()
        {
            using (SqlConnection conn = new SqlConnection(Configuration.FreightEstimatorSqlConnection))
            {
                conn.Open();

                Request request = FindNext(conn);
                if (request == null) return 0;
                if (!TryClaim(conn, request)) return 0;

                try
                {
                    return Answer(conn, request) ? 1 : 0;
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, request, "CostPlus request {LoginId}/{QtyNumber} failed: {Message}", ex);
                    throw;
                }
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
        }

        /// <summary>The columns <see cref="ReadRequest"/> reads, in order.</summary>
        internal const string RequestColumns =
            "f.LoginId, f.QtyNumber, f.DateRequested, f.ToAddress, f.ToCity, f.ToState, f.ToZip, f.ToCountry, f.FromState, " +
            "f.NumPackages, f.PkgWeight, f.LastPkgWeight ";

        private static Request ReadRequest(SqlDataReader r)
        {
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
                FromState = r.IsDBNull(8) ? null : r.GetString(8),
                NumPackages = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                PkgWeight = r.IsDBNull(10) ? 0 : r.GetInt32(10),
                LastPkgWeight = r.IsDBNull(11) ? 0 : r.GetInt32(11)
            };
        }

        /// <summary>
        /// The oldest request that is waiting for this poller (<see cref="WaitingWhere"/>).
        /// </summary>
        private static Request FindNext(SqlConnection conn)
        {
            using (SqlCommand cmd = new SqlCommand(null, conn))
            {
                cmd.Parameters.Add("@lease", SqlDbType.Int).Value = ClaimLeaseMinutes;
                cmd.CommandText =
                    "SELECT TOP 1 " + RequestColumns +
                    "FROM dbo.FreightRequests f " +
                    "WHERE " + WaitingWhere +
                    "ORDER BY f.DateRequested, f.LoginId, f.QtyNumber";

                using (SqlDataReader r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return ReadRequest(r);
                }
            }
        }

        /// <summary>
        /// Takes the claim. The unique key on (LoginId, QtyNumber, DateRequested) is the guard: a second cycle that
        /// reaches the same row inserts nothing. It may still retake the claim, but only if it is past its lease and was
        /// never completed, in one UPDATE that matches on exactly that, so two cycles cannot both retake it.
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
                    return RetakeExpiredClaim(conn, request);   // somebody has claimed it; unless that claim has expired, it is theirs
                }
            }
        }

        private bool RetakeExpiredClaim(SqlConnection conn, Request request)
        {
            using (SqlCommand cmd = new SqlCommand(
                "UPDATE dbo.CostPlusRequestClaim SET ClaimedBy = @by, ClaimedAt = SYSDATETIME() " +
                "WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d " +
                "  AND CompletedAt IS NULL AND ClaimedAt < DATEADD(MINUTE, -@lease, SYSDATETIME())", conn))
            {
                AddKey(cmd, request);
                cmd.Parameters.Add("@by", SqlDbType.VarChar, 100).Value = _instance;
                cmd.Parameters.Add("@lease", SqlDbType.Int).Value = ClaimLeaseMinutes;
                return cmd.ExecuteNonQuery() == 1;
            }
        }

        /// <summary>What the poller made of a request: the shaped result, or the error to write instead. Nothing is written yet.</summary>
        private class Outcome
        {
            public Outcome(Guid? quoteId, CostPlusResultShape.Shaped shaped, string errorCode, string errorDescription)
            {
                QuoteId = quoteId; Shaped = shaped; ErrorCode = errorCode; ErrorDescription = errorDescription;
            }
            public readonly Guid? QuoteId;
            public readonly CostPlusResultShape.Shaped Shaped;
            public readonly string ErrorCode, ErrorDescription;
        }

        /// <summary>
        /// Answers the request: a rate, or an error in the request's row (ErrorCode01 and ErrorDesc01, the console app's way) for
        /// whatever cannot be answered. Returns whether this instance's answer was the one written.
        /// </summary>
        private bool Answer(SqlConnection conn, Request request)
        {
            Outcome o = Compute(conn, request, shadow: false);
            return WriteResult(conn, request, o.QuoteId, o.Shaped, o.ErrorCode, o.ErrorDescription);
        }

        private Outcome Compute(SqlConnection conn, Request request, bool shadow)
        {
            var noRates = new CostPlusResultShape.Shaped();

            // A probe (#35) only tests that the poller can claim a row and write its result back: no Plant, no Quote, no UPS call,
            // so nothing it does can reach UPS or reporting.
            if (CostPlusProbe.IsProbe(request.LoginId)) return new Outcome(null, noRates, null, null);

            if (!CostPlusPlantMapping.TryPlantFor(request.FromState, out string plantCode, out string mappingProblem))
            {
                return new Outcome(null, noRates, General, mappingProblem);
            }
            if (!PlantDirectory.ValidCodes().Contains(plantCode))
            {
                return new Outcome(null, noRates, General, "Plant " + plantCode + " is not available for quotes");
            }

            // The shadow run (#36) leaves no Quote and spends no Reference Number; the quote ID only ties the carrier calls together.
            int referenceNumber = 0;
            Guid quoteId = Guid.NewGuid();
            if (!shadow)
            {
                int consumerId = ConsumerId(conn);
                referenceNumber = NextReferenceNumber(conn);
                QuoteLog.Record(quoteId, consumerId, referenceNumber.ToString(), plantCode, JsonConvert.SerializeObject(request));
            }

            // The console app's weight rule, word for word: (packages - 1) x package weight + last package weight, over 19,999.
            if (((long)(request.NumPackages - 1) * request.PkgWeight) + request.LastPkgWeight > MaxShipmentWeightLb)
            {
                return new Outcome(quoteId, noRates, WeightError, "Weight Exceeds Limit – Call");
            }

            // Without a Plant Charges row the unified module would price a service at its raw Carrier Rate. Never return that.
            List<string> carriersWithoutCharges = CarriersWithoutPlantCharges(plantCode);
            if (carriersWithoutCharges.Count > 0)
            {
                return new Outcome(quoteId, noRates, General,
                    "Plant Charges are missing for plant " + plantCode + " (" + string.Join(", ", carriersWithoutCharges) + ")");
            }

            // Only Services that are available are quoted; the others are named in the error (CONTEXT.md: Service Availability).
            var parcelServices = ServiceCatalog.All.Where(s => s.Carrier == ServiceCatalog.Ups && s.Name != RateCalculationsChargeSchedule.GroundFreightServiceName).ToList();
            List<string> available = parcelServices.Where(ServiceCatalog.IsAvailable).Select(s => s.Name).ToList();
            string notAvailable = string.Join(", ", parcelServices.Select(s => s.Name).Except(available));
            string notAvailableError = notAvailable.Length == 0 ? null : "Service not available: " + notAvailable;
            if (available.Count == 0)
            {
                return new Outcome(quoteId, noRates, General, notAvailableError);
            }

            UpsParcelEstimate estimate = new UpsEstimationService().EstimateParcel(new UpsQuoteRequest
            {
                PlantCode = plantCode,
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
                ParcelServiceNames = available,
                TimeoutMs = UpsTimeoutMs
            });

            // The console app validates the address first and gives up when UPS returns more than one candidate.
            // (UPS has already been asked to rate it by now: the estimator checks the address and rates in one call. One discarded call.)
            if (estimate.AddressCandidateCount > 1)
            {
                return new Outcome(quoteId, noRates, AddressError, "Invalid address -- unable to validate");
            }

            if (estimate.Status != CarrierStatus.Ok)
            {
                return new Outcome(quoteId, noRates, General, "Error processing request: " + estimate.Message);
            }

            CostPlusResultShape.Shaped shaped = CostPlusResultShape.Shape(plantCode, estimate.Services, estimate.AddressClassification, request.ToZip);
            return new Outcome(quoteId, shaped, notAvailableError == null ? null : General, notAvailableError);
        }

        /// <summary>
        /// Writes the answer and completes the claim together. If the request was answered meanwhile (this instance stalled past
        /// its lease and another retook the claim and answered), nothing is written and the claim is left as the other left it.
        /// Returns whether this instance's answer was the one written. An error, if any, goes in the first error slot and
        /// clears the others, as the console app does.
        /// </summary>
        private bool WriteResult(SqlConnection conn, Request request, Guid? quoteId, CostPlusResultShape.Shaped shaped, string errorCode, string errorDescription)
        {
            if (errorCode != null)
                Log(LogLevel.Warning, request, "CostPlus request {LoginId}/{QtyNumber} was answered with " + errorCode + ": {Message}", errorDescription);

            using (SqlTransaction tx = conn.BeginTransaction())
            {
                using (SqlCommand cmd = new SqlCommand(null, conn, tx))
                {
                    var assignments = new List<string> { "DateProcessed = GETDATE()", "DateRated = GETDATE()" };
                    List<CostPlusResultShape.Slot> slots = shaped.Slots;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        string n = (i + 1).ToString("00");
                        assignments.Add("Carrier" + n + " = @c" + i);
                        assignments.Add("Service" + n + " = @s" + i);
                        assignments.Add("Rate" + n + " = @r" + i);
                        assignments.Add("Note" + n + " = @n" + i);
                        cmd.Parameters.Add("@c" + i, SqlDbType.VarChar, 50).Value = ServiceCatalog.Ups;
                        cmd.Parameters.Add("@s" + i, SqlDbType.VarChar, 50).Value = slots[i].ServiceName;
                        cmd.Parameters.Add("@n" + i, SqlDbType.VarChar, 200).Value = slots[i].Note.ToString();
                        cmd.Parameters.Add("@r" + i, SqlDbType.Decimal).Value = slots[i].Rate;
                    }
                    if (slots.Count > 0)
                    {
                        assignments.Add("ClassifiedResidential = @cr");
                        assignments.Add("SubstitutedZip = @sz");
                        cmd.Parameters.Add("@cr", SqlDbType.Bit).Value = shaped.ClassifiedResidential;
                        cmd.Parameters.Add("@sz", SqlDbType.VarChar, 50).Value = (object)shaped.SubstitutedZip ?? DBNull.Value;
                    }
                    if (errorCode != null)
                    {
                        assignments.Add("ErrorCode01 = @ec");
                        assignments.Add("ErrorDesc01 = @ed");
                        assignments.Add("ErrorCode02 = NULL, ErrorDesc02 = NULL, ErrorCode03 = NULL, ErrorDesc03 = NULL");
                        cmd.Parameters.Add("@ec", SqlDbType.VarChar, 10).Value = errorCode;
                        cmd.Parameters.Add("@ed", SqlDbType.VarChar, 1000).Value = errorDescription;
                    }
                    cmd.CommandText = "UPDATE dbo.FreightRequests SET " + string.Join(", ", assignments) +
                        " WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d AND (DateProcessed IS NULL OR DateRated IS NULL)";   // the same "not done yet" rule FindNext uses
                    AddKey(cmd, request);
                    if (cmd.ExecuteNonQuery() == 0)
                    {
                        tx.Rollback();
                        return false;
                    }
                }

                using (SqlCommand cmd = new SqlCommand(
                    "UPDATE dbo.CostPlusRequestClaim SET CompletedAt = SYSDATETIME(), QuoteId = @quote WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d", conn, tx))
                {
                    AddKey(cmd, request);
                    cmd.Parameters.Add("@quote", SqlDbType.UniqueIdentifier).Value = (object)quoteId ?? DBNull.Value;
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return true;
            }
        }

        /// <summary>Logs a failure with the request's LoginId and QtyNumber (the row's identity, ADR 0005).</summary>
        private void Log(LogLevel level, Request request, string template, object detail)
        {
            if (_logger != null)
                _logger.Log(level, detail as Exception, template, request.LoginId, request.QtyNumber, (detail as Exception)?.Message ?? detail);
            else
                System.Diagnostics.Trace.TraceWarning(string.Format(template.Replace("{LoginId}", "{0}").Replace("{QtyNumber}", "{1}").Replace("{Message}", "{2}"), request.LoginId, request.QtyNumber, (detail as Exception)?.Message ?? detail));
        }

        private static void AddKey(SqlCommand cmd, Request request)
        {
            cmd.Parameters.Add("@l", SqlDbType.VarChar, 50).Value = request.LoginId;
            cmd.Parameters.Add("@q", SqlDbType.TinyInt).Value = request.QtyNumber;
            cmd.Parameters.Add("@d", SqlDbType.DateTime).Value = request.DateRequested;
        }

        /// <summary>The carriers whose Plant Charges the plant lacks: UPS for non-CWT services, UPSCWT for CWT ones (see RateCalculationsChargeSchedule).</summary>
        private static List<string> CarriersWithoutPlantCharges(string plantCode)
        {
            var missing = new List<string>();
            if (!new RateCalculations(0, RateCalculations.Carriers.UPS).PerPackageCharge.ContainsKey(plantCode)) missing.Add("UPS");
            if (!new RateCalculations(0, RateCalculations.Carriers.UPSCWT).PerPackageChargeCWT.ContainsKey(plantCode)) missing.Add("UPSCWT");
            return missing;
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
