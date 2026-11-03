using AuthenticationServer.Models.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The UPS estimation service (#16) behind the Plant page: UPS parcel (GRID 1, single plant) and Ground Freight
    /// (GRID 2). It must quote exactly what the page quotes today, and it must not touch the request it is given.
    /// Same SANDBOX charge schedule and recorded carrier responses as the Plant page characterization tests (#15).
    /// </summary>
    public class UpsEstimationServiceTests
    {
        public static IEnumerable<TestCaseData> ParcelCases() => Cases("parcel");
        public static IEnumerable<TestCaseData> GroundFreightCases() => Cases("groundfreight");

        private static IEnumerable<TestCaseData> Cases(string kind) =>
            QuoteCase.All(kind).Select(c => new TestCaseData(c).SetName(kind + " " + c.CaseId + " " + c.Plant + " " + c.Packages + " pkgs"));

        [TestCaseSource(nameof(ParcelCases))]
        public void ParcelQuote_MatchesWhatThePlantPageShows(QuoteCase c)
        {
            JObject actual = QuoteFlow.RunService(c);
            Assert.That(actual.Count, Is.GreaterThan(0), "the service returned no services");
            PlantPageQuoteTests.AssertMatchesExpected(c, actual);
        }

        [TestCaseSource(nameof(GroundFreightCases))]
        public void GroundFreightQuote_MatchesWhatThePlantPageShows(QuoteCase c)
        {
            JObject actual = QuoteFlow.RunService(c);
            Assert.That(actual.Count, Is.GreaterThan(0), "the service returned no services");
            PlantPageQuoteTests.AssertMatchesExpected(c, actual);
        }

        [Test]
        public void DoesNotMutateTheRequestItWasGiven()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsQuoteRequest request = QuoteFlow.RequestFor(c);
            request.IncludeGroundFreight = true;
            string before = JsonConvert.SerializeObject(request);

            QuoteFlow.RunService(c, request, QuoteFlow.FakeFor(c));

            Assert.That(JsonConvert.SerializeObject(request), Is.EqualTo(before));
        }

        [Test]
        public void ReturnsWhatUpsCorrectedAboutTheAddress_InsteadOfWritingItIntoTheRequest()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsQuoteRequest request = QuoteFlow.RequestFor(c);
            var fake = QuoteFlow.FakeFor(c);

            UpsQuoteResult result;
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            using (fake.Install())
            {
                result = new UpsEstimationService().Quote(request);
            }

            // The recorded address validation answers "1 MAIN ST, PITTSBURGH PA 15201-1234, Commercial".
            Assert.That(result.Shipment.AddressClassification, Is.EqualTo("Commercial"));
            Assert.That(result.Shipment.CorrectedAddress, Is.EqualTo("1 MAIN ST"));
            Assert.That(result.Shipment.CorrectedCity, Is.EqualTo("PITTSBURGH"));
            Assert.That(result.Shipment.CorrectedState, Is.EqualTo("PA"));
            Assert.That(result.Shipment.Zip, Does.EndWith("-1234"));
            Assert.That(request.Address, Is.EqualTo("1 Main St"));
            Assert.That(request.Zip, Is.EqualTo(c.ShipToZip));
        }

        [Test]
        public void ParcelIsNotQuotedOverUpsShopsFiftyPackageLimit_AndUpsIsNotAskedForIt()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsQuoteRequest request = QuoteFlow.RequestFor(c);
            request.NumberOfPackages = 51;
            var fake = QuoteFlow.FakeFor(c);

            UpsQuoteResult result;
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            using (fake.Install())
            {
                result = new UpsEstimationService().Quote(request);
            }

            Assert.That(result.Parcel, Is.Null);
            Assert.That(fake.Requests.Select(r => r.Url), Has.None.Contains("rating/v2403/Shop"));
        }

        [Test]
        public void QuotesOnlyWhatWasAskedFor()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsQuoteRequest request = QuoteFlow.RequestFor(c);
            request.IncludeParcel = false;
            request.IncludeGroundFreight = false;
            var fake = QuoteFlow.FakeFor(c);

            UpsQuoteResult result;
            using (fake.Install())
            {
                result = new UpsEstimationService().Quote(request);
            }

            Assert.That(result.Parcel, Is.Null);
            Assert.That(result.GroundFreight, Is.Null);
            Assert.That(fake.Requests, Is.Empty);
        }
    }
}
