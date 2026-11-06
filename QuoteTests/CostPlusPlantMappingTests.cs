using AuthenticationServer.CostPlus;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The CostPlus request names no Plant, only a ship-from address (ADR 0005: a knowing exception to "don't infer the
    /// Plant", until CostPlus sends a plant code). The adapter maps the state to a Plant through an explicit table and reports
    /// in the request's row anything it cannot answer. Same seam as the rest of the poller: one poll cycle against the
    /// FreightEstimator test database with the recorded carrier fake.
    /// </summary>
    public class CostPlusPlantMappingTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        /// <summary>The known schedule with a different per-shipment charge for each plant, so a quote that picked up another plant's charges lands on a wrong number.</summary>
        private static SandboxPlantCharges.Row[] ScheduleWithPerShipmentRaisedBy(int extra) =>
            SandboxPlantCharges.KnownSchedule().Select(r => new SandboxPlantCharges.Row
            {
                CarrierId = r.CarrierId, PerPackage = r.PerPackage, PerShipment = r.PerShipment + extra,
                NextDayAir = r.NextDayAir, SecondDayAir = r.SecondDayAir, Ground = r.Ground, ThreeDaySelect = r.ThreeDaySelect,
                NextDayAirSaver = r.NextDayAirSaver, NextDayAirEarlyAM = r.NextDayAirEarlyAM, SecondDayAirAM = r.SecondDayAirAM, Saver = r.Saver
            }).ToArray();

        [TestCase("GA", "ALP", 0)]
        [TestCase("PA", "BUT", 1)]
        [TestCase("IN", "FTW", 2)]
        [TestCase("SC", "PDT", 3)]
        [TestCase("ME", "POR", 4)]
        public void EachMappedStateSelectsItsPlantsCharges(string state, string plant, int extraPerShipment)
        {
            _costPlus.SetPlantQuotable(plant, true);
            SandboxCostPlus.Request request = SandboxCostPlus.For(Case);
            request.FromState = state;
            string loginId = _costPlus.Add(request);

            Assert.That(CostPlusCycle.Run(QuoteFlow.FakeFor(Case), plant, ScheduleWithPerShipmentRaisedBy(extraPerShipment)), Is.EqualTo(1));

            // UPS3DaySelect, not CWT: published 312.30 + 6% markup 18.738 + 6 packages x 2.00 + (5.00 + this plant's extra) = 348.038 + extra.
            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.Null, row.ErrorDesc01);
            Assert.That(row.Slots.Single(s => s.Service == "UPS3DaySelect").Rate, Is.EqualTo(348.04m + extraPerShipment));
            Assert.That(_costPlus.QuoteOf(_costPlus.ClaimOf(loginId).QuoteId.Value).PlantCode, Is.EqualTo(plant));
        }

        [TestCase("TX", "Ship-from state 'TX' is not mapped to a Plant")]
        [TestCase(null, "Ship-from state is missing")]
        public void AFromStateThatMapsToNoPlantIsRejectedWithAGeneralError(string state, string description)
        {
            SandboxCostPlus.Request request = SandboxCostPlus.For(Case);
            request.FromState = state;
            string loginId = _costPlus.Add(request);
            var fake = new FakeCarrierHttp();   // nothing set up: any carrier call throws

            Assert.That(CostPlusCycle.Run(fake), Is.EqualTo(1), "the request is answered, with an error");

            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.EqualTo("GENERAL"));
            Assert.That(row.ErrorDesc01, Is.EqualTo(description));
            Assert.That(row.DateProcessed, Is.Not.Null);
            Assert.That(row.DateRated, Is.Not.Null);
            Assert.That(row.Slots, Is.Empty, "no rate is returned");
            Assert.That(_costPlus.ClaimOf(loginId).CompletedAt, Is.Not.Null, "the request is finished, not left claimed");
            Assert.That(_costPlus.QuoteCount(loginId), Is.EqualTo(0), "no Plant, so no Quote can be logged");
            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void AMappedPlantThatCannotBeQuotedIsRejectedWithAGeneralError()
        {
            _costPlus.SetPlantQuotable("PDT", false);   // inactive and without a UPS shipping number, as in the real data
            SandboxCostPlus.Request request = SandboxCostPlus.For(Case);
            request.FromState = "SC";
            string loginId = _costPlus.Add(request);
            var fake = new FakeCarrierHttp();

            Assert.That(CostPlusCycle.Run(fake, "PDT"), Is.EqualTo(1));

            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.EqualTo("GENERAL"));
            Assert.That(row.ErrorDesc01, Is.EqualTo("Plant PDT is not available for quotes"));
            Assert.That(row.Slots, Is.Empty);
            Assert.That(fake.Requests, Is.Empty);
        }
    }
}
