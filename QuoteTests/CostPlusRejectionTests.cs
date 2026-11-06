using AuthenticationServer.CostPlus;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// What the CostPlus poller does with a request it cannot (fully) answer (#33, ADR 0005): the reason goes into the request's own
    /// row, in the first error slot, where FileMaker reads it, and no rate that would be wrong is ever returned. WEIGHT and ADDRESS
    /// are the console app's codes and wording; everything else is GENERAL with a specific description. Same seam as the rest of the
    /// poller: one poll cycle against the FreightEstimator test database with the recorded carrier fake.
    /// </summary>
    public class CostPlusRejectionTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        private string Add(Action<SandboxCostPlus.Request> change = null)
        {
            SandboxCostPlus.Request request = SandboxCostPlus.For(Case);
            change?.Invoke(request);
            return _costPlus.Add(request);
        }

        private void AssertRejected(string loginId, string code, string description)
        {
            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.EqualTo(code));
            Assert.That(row.ErrorDesc01, Is.EqualTo(description));
            Assert.That(row.DateProcessed, Is.Not.Null);
            Assert.That(row.DateRated, Is.Not.Null);
            Assert.That(row.Slots, Is.Empty, "no rate is returned");
            Assert.That(_costPlus.ClaimOf(loginId).CompletedAt, Is.Not.Null, "the request is finished, not left claimed");
        }

        // ---- WEIGHT: (packages - 1) x package weight + last package weight must not pass 19,999 ------------------

        [Test]
        public void AShipmentOverTheWeightLimitIsRejectedWithAWeightError()
        {
            string loginId = Add(r => { r.NumPackages = 21; r.PkgWeight = 1000; r.LastPkgWeight = 0; });   // 20 x 1000 + 0 = 20,000
            var fake = new FakeCarrierHttp();   // nothing set up: any carrier call throws

            Assert.That(CostPlusCycle.Run(fake), Is.EqualTo(1));

            AssertRejected(loginId, "WEIGHT", "Weight Exceeds Limit – Call");
            Assert.That(fake.Requests, Is.Empty);
            Assert.That(_costPlus.QuoteCount(loginId), Is.EqualTo(1), "the request is still stored as a Quote");
        }

        [Test]
        public void AShipmentAtTheWeightLimitIsStillQuoted()
        {
            string loginId = Add(r => { r.NumPackages = 20; r.PkgWeight = 1000; r.LastPkgWeight = 999; });   // 19 x 1000 + 999 = 19,999

            Assert.That(CostPlusCycle.Run(QuoteFlow.FakeFor(Case)), Is.EqualTo(1));

            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.Null, row.ErrorDesc01);
            Assert.That(row.Slots, Is.Not.Empty);
        }

        // ---- a missing Plant Charges row is an error, never a raw Carrier Rate ----------------------------------

        [TestCase(new string[0], "UPS, UPSCWT")]
        [TestCase(new[] { "UPS" }, "UPSCWT")]
        [TestCase(new[] { "UPSCWT" }, "UPS")]
        public void MissingPlantChargesAreReportedAndNoRateIsReturned(string[] carriersWithCharges, string missing)
        {
            string loginId = Add();
            var fake = new FakeCarrierHttp();
            SandboxPlantCharges.Row[] schedule = SandboxPlantCharges.KnownSchedule()
                .Where(r => (r.CarrierId != "UPS" && r.CarrierId != "UPSCWT") || carriersWithCharges.Contains(r.CarrierId)).ToArray();

            Assert.That(CostPlusCycle.Run(fake, "ALP", schedule), Is.EqualTo(1));

            AssertRejected(loginId, "GENERAL", "Plant Charges are missing for plant ALP (" + missing + ")");
            Assert.That(fake.Requests, Is.Empty, "UPS is not asked for a rate that cannot be priced");
        }

        // ---- ADDRESS: UPS cannot settle on one address (more than one candidate), as in the console app ---------

        [Test]
        public void AnAddressUpsCannotSettleOnIsRejectedWithAnAddressError()
        {
            string loginId = Add();
            // tests/Recorded/ups-address-validation-ambiguous.json is the recorded commercial answer with a second candidate added.
            FakeCarrierHttp fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-ambiguous.json")
                .RespondWith("rating/v2403/Shop", System.Net.HttpStatusCode.OK, Case.ResponseText);

            Assert.That(CostPlusCycle.Run(fake), Is.EqualTo(1));

            AssertRejected(loginId, "ADDRESS", "Invalid address -- unable to validate");
        }

        // ---- UPS did not give an answer: the request is answered with an error, not left claimed ----------------

        [Test]
        public void AUpsTimeoutIsReportedAsAGeneralErrorAndTheRequestIsFinished()
        {
            string loginId = Add();
            FakeCarrierHttp fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .TimeOut("rating/v2403/Shop");

            Assert.That(CostPlusCycle.Run(fake), Is.EqualTo(1));

            AssertRejected(loginId, "GENERAL", "Error processing request: UPS did not answer in time.");
        }

        // ---- a Service that is not available is named, never quoted ---------------------------------------------

        [Test]
        public void AServiceThatIsNotAvailableIsNamedInAnErrorAndLeftOutOfTheRates()
        {
            string loginId = Add();
            string[] allButGround = CostPlusCycle.ParcelServices.Where(s => s != "UPSGround").ToArray();

            Assert.That(CostPlusCycle.Run(QuoteFlow.FakeFor(Case), available: allButGround), Is.EqualTo(1));

            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.EqualTo("GENERAL"));
            Assert.That(row.ErrorDesc01, Is.EqualTo("Service not available: UPSGround"));
            Assert.That(row.Slots.Select(s => s.Service), Does.Not.Contain("UPSGround"));
            Assert.That(row.Slots.Single(s => s.Service == "UPS3DaySelect").Rate, Is.EqualTo(348.04m), "the services that are available are still quoted");
        }

        [Test]
        public void WhenNoServiceIsAvailableNothingIsQuotedAndUpsIsNotAsked()
        {
            string loginId = Add();
            var fake = new FakeCarrierHttp();

            Assert.That(CostPlusCycle.Run(fake, available: new string[0]), Is.EqualTo(1));

            AssertRejected(loginId, "GENERAL", "Service not available: " + string.Join(", ", CostPlusCycle.ParcelServices));
            Assert.That(fake.Requests, Is.Empty);
        }
    }
}
