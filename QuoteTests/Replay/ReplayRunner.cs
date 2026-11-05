using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Net;
using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests.Replay
{
    public class ReplayOutcome
    {
        public List<ReplayRow> Rows { get; } = new List<ReplayRow>();

        /// <summary>Stored calls that were read but could not be replayed, each with why. Never silently dropped.</summary>
        public List<string> Skipped { get; } = new List<string>();
    }

    /// <summary>
    /// Replays stored carrier responses (#26) through today's formula and through the unified module, with the carrier
    /// HTTP replaced by a fake so no carrier is called. Today's formula is the real one: the estimation services'
    /// legacy Quote path, run on a request rebuilt from the stored call, against the Plant Charges now in SANDBOX.
    /// The unified side is the Quote API's own path (EstimateParcel, EstimateGroundFreight, CalculatedRate.Apply).
    ///
    /// The legacy path logs as it always has (Requests_Rate, Log_LTL, QuoteServiceCalculation, the shadow log). So that a
    /// replay does not feed the next one, <see cref="Run"/> removes what it logged, in SANDBOX, before it returns.
    /// </summary>
    public static class ReplayRunner
    {
        public static ReplayOutcome Run(StoredResponses stored)
        {
            SandboxGuard.Require();
            var outcome = new ReplayOutcome();
            outcome.Skipped.AddRange(stored.Skipped);

            var quoteIds = new List<Guid>();
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");   // the Logistics Partner total is a currency string
            using (var cleanup = new LoggedRowsCleanup(quoteIds))
            {
                try
                {
                    foreach (StoredUpsResponse call in stored.Ups)
                    {
                        try { ReplayUps(call, outcome, quoteIds); }
                        catch (Exception ex) { outcome.Skipped.Add(call.Source + ": " + ex.Message); }
                    }
                    foreach (StoredLtlResponse call in stored.Ltl)
                    {
                        try { ReplayLtl(call, outcome, quoteIds); }
                        catch (Exception ex) { outcome.Skipped.Add(call.Source + ": " + ex.Message); }
                    }
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = previous;
                }
            }
            return outcome;
        }

        private static void ReplayUps(StoredUpsResponse call, ReplayOutcome outcome, List<Guid> quoteIds)
        {
            var request = new UpsQuoteRequest
            {
                PlantCode = call.Plant,
                QuoteId = NewQuoteId(quoteIds),
                UserName = "replay",
                AccountNumber = "0",
                Address = call.Address,
                City = call.City,
                State = call.State,
                Zip = call.Zip,
                Country = call.Country,
                PackageWeights = call.Weights,
                IncludeParcel = !call.GroundFreight,
                IncludeGroundFreight = call.GroundFreight
            };

            // Address validation is not what is stored; any recorded answer will do, since it does not change a price.
            // The rate call answers with the stored response, whichever UPS endpoint it was.
            var fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/", HttpStatusCode.OK, call.ResponseJson);

            using (fake.Install())
            {
                var service = new UpsEstimationService();
                UpsQuoteResult legacy = service.Quote(request);

                if (call.GroundFreight)
                {
                    UpsGroundFreightEstimate unified = service.EstimateGroundFreight(request);
                    UPSService[] services = legacy.GroundFreight?.UPSServices;
                    if (unified.Status != CarrierStatus.Ok || services == null || services.Length == 0)
                        throw new InvalidOperationException("ground freight did not quote (" + unified.Status + " " + unified.Message + ")");

                    outcome.Rows.Add(Row(ReplayRow.Grid2, call.Plant, "UPSGroundFreight", false, unified.CalculatedRate, (decimal)services[0].CustomerRate, call.Source));
                    return;
                }

                UpsParcelEstimate parcel = service.EstimateParcel(request);
                if (parcel.Status != CarrierStatus.Ok || legacy.Parcel == null)
                    throw new InvalidOperationException("parcel did not quote (" + parcel.Status + " " + parcel.Message + ")");

                foreach (UpsParcelServiceEstimate estimate in parcel.Services)
                {
                    // Plant Charges are applied to these services only, so they are the only ones with a legacy value to compare.
                    UPSService old = legacy.Parcel.UPSServices.FirstOrDefault(s => s.ServiceName == estimate.ServiceName);
                    if (old == null) continue;
                    bool cwt = string.Equals(old.CWT, "Yes", StringComparison.OrdinalIgnoreCase);
                    outcome.Rows.Add(Row(ReplayRow.Grid1Single, call.Plant, estimate.ServiceName, cwt, estimate.CalculatedRate, (decimal)old.CustomerRate, call.Source));
                }
            }
        }

        private static void ReplayLtl(StoredLtlResponse call, ReplayOutcome outcome, List<Guid> quoteIds)
        {
            if (string.IsNullOrWhiteSpace(call.Plant) || call.Plant.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("not a single-plant call");

            var request = new LogisticsPartnerQuoteRequest
            {
                PlantCode = call.Plant,
                QuoteId = NewQuoteId(quoteIds),
                UserName = "replay",
                AccountNumber = "0",
                CorrectedCity = call.City,
                State = call.State,
                Zip = call.Zip,
                Country = call.Country,
                PackageWeights = call.Weights,
                FreightClass = call.FreightClass,
                PickupDate = call.PickupDate
            };

            string host = new Uri(AuthenticationServer.Models.Configuration.TransportInsightRateUrl).Host;
            var fake = new FakeCarrierHttp().RespondWith(host, HttpStatusCode.OK, call.ResponseXml);

            using (fake.Install())
            {
                UPSService[] options = new LogisticsPartnerEstimationService().Quote(request).Rates?.UPSServices;
                if (options == null || options.Length == 0) throw new InvalidOperationException("the Logistics Partner replay produced no options");

                foreach (UPSService option in options)
                {
                    CalculatedRate unified = CalculatedRate.Apply((decimal)option.CustomerRate, new RateCalculationsChargeSchedule(), new RatingContext
                    {
                        PlantCode = call.Plant,
                        ServiceName = RateCalculationsChargeSchedule.LtlServiceName,
                        PackageCount = call.Weights.Count,
                        IsCwtEligible = false
                    });
                    // What the Plant page shows for the option is a currency string.
                    decimal legacy = decimal.Parse(option.TotalCost, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"));
                    outcome.Rows.Add(Row(ReplayRow.Ltl, call.Plant, option.ServiceName, false, unified, legacy, call.Source));
                }
            }
        }

        private static ReplayRow Row(string callSite, string plant, string service, bool cwt, CalculatedRate unified, decimal legacy, string source) =>
            new ReplayRow
            {
                CallSite = callSite,
                Plant = plant,
                Service = service,
                IsCwtEligible = cwt,
                CarrierRate = unified.CarrierRate,
                MarkupPercent = unified.MarkupPercent,
                Legacy = legacy,
                Unified = unified.RoundedAmount,
                Source = source
            };

        private static Guid NewQuoteId(List<Guid> quoteIds)
        {
            Guid id = Guid.NewGuid();
            quoteIds.Add(id);
            return id;
        }

        /// <summary>
        /// Removes, from SANDBOX, the rows a replay's legacy path logged: Requests_Rate and Log_LTL rows added since the
        /// replay began, and the per-Quote rows for the Quote IDs it made up. Disposed after the replay.
        /// </summary>
        private sealed class LoggedRowsCleanup : IDisposable
        {
            private readonly List<Guid> _quoteIds;
            private readonly int _ratesAfter;
            private readonly int _ltlAfter;

            public LoggedRowsCleanup(List<Guid> quoteIds)
            {
                _quoteIds = quoteIds;
                _ratesAfter = Max("SELECT ISNULL(MAX(RequestId), 0) FROM dbo.Requests_Rate");
                _ltlAfter = Max("SELECT ISNULL(MAX(LTLRequestId), 0) FROM dbo.Log_LTL");
            }

            public void Dispose()
            {
                using (var conn = Open())
                {
                    Exec(conn, "DELETE FROM dbo.Requests_Rate WHERE RequestId > @after", _ratesAfter);
                    Exec(conn, "DELETE FROM dbo.Log_LTL WHERE LTLRequestId > @after", _ltlAfter);
                    foreach (Guid id in _quoteIds)
                    {
                        using (var cmd = new SqlCommand("DELETE FROM dbo.QuoteServiceCalculation WHERE QuoteId = @q; DELETE FROM dbo.CalculatedRateShadowLog WHERE QuoteId = @q", conn))
                        {
                            cmd.Parameters.AddWithValue("@q", id);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }

            private static int Max(string sql)
            {
                using (var conn = Open())
                using (var cmd = new SqlCommand(sql, conn)) return Convert.ToInt32(cmd.ExecuteScalar());
            }

            private static void Exec(SqlConnection conn, string sql, int after)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@after", after);
                    cmd.ExecuteNonQuery();
                }
            }

            private static SqlConnection Open()
            {
                var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["UpsRate"].ConnectionString);
                conn.Open();
                return conn;
            }
        }
    }
}
