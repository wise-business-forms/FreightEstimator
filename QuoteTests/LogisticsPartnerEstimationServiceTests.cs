using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The Logistics Partner (LTL) estimation service (#17) behind the Plant page. It must quote exactly what the
    /// page quotes today, send the Logistics Partner what the page sends today, and not touch the request it is given.
    /// Same SANDBOX charge schedule and recorded carrier responses as the Plant page characterization tests (#15).
    /// </summary>
    public class LogisticsPartnerEstimationServiceTests
    {
        public static IEnumerable<TestCaseData> LtlCases() =>
            QuoteCase.All("ltl").Select(c => new TestCaseData(c).SetName("ltl " + c.CaseId + " " + c.Plant + " " + c.Packages + " pkgs"));

        [TestCaseSource(nameof(LtlCases))]
        public void LtlQuote_MatchesWhatThePlantPageShows(QuoteCase c)
        {
            JObject actual = QuoteFlow.RunLtlService(c);
            Assert.That(actual.Count, Is.GreaterThan(0), "the service returned no services");
            PlantPageQuoteTests.AssertMatchesExpected(c, actual);
        }

        [Test]
        public void DoesNotMutateTheRequestItWasGiven()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LogisticsPartnerQuoteRequest request = QuoteFlow.LtlRequestFor(c);
            request.AccountNumber = "";   // the service defaults a blank account to "0"
            string before = JsonConvert.SerializeObject(request);

            QuoteFlow.RunLtlService(c, request, QuoteFlow.LtlFakeFor(c));

            Assert.That(JsonConvert.SerializeObject(request), Is.EqualTo(before));
        }

        [Test]
        public void ReportsTheDefaultsItAppliedInsteadOfWritingThemIntoTheRequest()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LogisticsPartnerQuoteRequest request = QuoteFlow.LtlRequestFor(c);
            request.AccountNumber = "";
            request.Country = "US";

            LogisticsPartnerQuoteResult result = Quote(c, request, QuoteFlow.LtlFakeFor(c));

            // The page shows these two, so it has to be told. The Logistics Partner is sent "USA".
            Assert.That(result.Outcome.AccountNumber, Is.EqualTo("0"));
            Assert.That(result.Outcome.Country, Is.EqualTo("USA"));
            Assert.That(request.AccountNumber, Is.EqualTo(""));
            Assert.That(request.Country, Is.EqualTo("US"));
        }

        [Test]
        public void ReportsTheRequestAndResponseItExchangedWithTheLogisticsPartner()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            var fake = QuoteFlow.LtlFakeFor(c);

            LogisticsPartnerQuoteResult result = Quote(c, QuoteFlow.LtlRequestFor(c), fake);

            Assert.That(result.Outcome.RequestMessage, Is.EqualTo(fake.Requests.Last().Body));
            Assert.That(result.Outcome.ResponseMessage, Is.EqualTo(c.ResponseText));
        }

        [Test]
        public void SendsTheCityUpsCorrected_AndNothingWhenUpsCorrectedNothing()
        {
            QuoteCase c = QuoteCase.All("ltl").First();

            var corrected = QuoteFlow.LtlFakeFor(c);
            Quote(c, QuoteFlow.LtlRequestFor(c), corrected);
            Assert.That(corrected.Requests.Last().Body, Does.Contain("<City>PITTSBURGH</City><State>" + c.ShipToState));

            // Pinned as the Plant page behaves today: with no UPS correction the drop city is sent empty.
            var uncorrected = QuoteFlow.LtlFakeFor(c);
            LogisticsPartnerQuoteRequest request = QuoteFlow.LtlRequestFor(c);
            request.CorrectedCity = null;
            Quote(c, request, uncorrected);
            Assert.That(uncorrected.Requests.Last().Body, Does.Contain("<City></City><State>" + c.ShipToState));
        }

        [Test]
        public void SendsTheAccessorialsThatWereAskedFor()
        {
            QuoteCase c = QuoteCase.All("ltl").First();

            var none = QuoteFlow.LtlFakeFor(c);
            Quote(c, QuoteFlow.LtlRequestFor(c), none);
            Assert.That(none.Requests.Last().Body, Does.Contain("<ServiceFlags />"));

            var asked = QuoteFlow.LtlFakeFor(c);
            LogisticsPartnerQuoteRequest request = QuoteFlow.LtlRequestFor(c);
            request.Accessorials = new LtlAccessorials { LiftgateDelivery = true, ResidentialDelivery = true };
            Quote(c, request, asked);
            Assert.That(asked.Requests.Last().Body, Does.Contain("<ServiceFlags><ServiceFlag code=\"LIFT\" /><ServiceFlag code=\"RESD\" /></ServiceFlags>"));
        }

        private static LogisticsPartnerQuoteResult Quote(QuoteCase c, LogisticsPartnerQuoteRequest request, FakeCarrierHttp fake)
        {
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            using (fake.Install())
            {
                return new LogisticsPartnerEstimationService().Quote(request);
            }
        }
    }
}
