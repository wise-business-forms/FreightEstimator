using AuthenticationServer.CostPlus;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The result row as FileMaker reads it (#34, ADR 0005): the console app's shaping, copied. The rules are tested directly on
    /// the shaper; the last tests check the same things arrive in the row from a poll cycle.
    /// </summary>
    public class CostPlusResultShapeTests
    {
        private class Flat : IChargeSchedule
        {
            public PlantServiceCharges GetCharges(string plantCode, string serviceName, bool isCwtEligible) => new PlantServiceCharges();
        }

        private static UpsParcelServiceEstimate Service(string name, decimal rate) => new UpsParcelServiceEstimate
        {
            ServiceName = name,
            CalculatedRate = CalculatedRate.Apply(rate, new Flat(), new RatingContext { PlantCode = "ALP", ServiceName = name, PackageCount = 1 })
        };

        [Test]
        public void ServicesAreOrderedCheapestFirst()
        {
            var shaped = CostPlusResultShape.Shape("BUT", new[] { Service("B", 30m), Service("A", 10m), Service("C", 20m) }, "Commercial", "02379");

            Assert.That(shaped.Slots.Select(s => s.ServiceName), Is.EqualTo(new[] { "A", "C", "B" }));
        }

        [Test]
        public void AMethodNamedXxIsDropped()
        {
            var shaped = CostPlusResultShape.Shape("ALP", new[] { Service("XX", 5m), Service("A", 10m) }, "Commercial", "02379");

            Assert.That(shaped.Slots.Select(s => s.ServiceName), Is.EqualTo(new[] { "A" }));
        }

        [TestCase("ALP"), TestCase("FTW")]
        public void AlpAndFtwKeepUpsServicesButNotGroundFreight(string plant)
        {
            var shaped = CostPlusResultShape.Shape(plant, new[] { Service("UPSGroundFreight", 5m), Service("UPSGround", 10m) }, "Commercial", "02379");

            Assert.That(shaped.Slots.Select(s => s.ServiceName), Is.EqualTo(new[] { "UPSGround" }));
        }

        [Test]
        public void AtMostFifteenSlotsAreKeptAfterTheDroppedMethodsAreGone()
        {
            var services = Enumerable.Range(1, 20).Select(i => Service("S" + i, i)).Concat(new[] { Service("XX", 0.01m) });

            var shaped = CostPlusResultShape.Shape("ALP", services, "Commercial", "02379");

            Assert.That(shaped.Slots, Has.Count.EqualTo(15));
            Assert.That(shaped.Slots[0].ServiceName, Is.EqualTo("S1"), "XX was dropped before the cut");
        }

        [TestCase("Residential", 2, true)]
        [TestCase("Commercial", 1, false)]
        [TestCase("Unknown", 0, false)]
        [TestCase("", 0, false)]
        [TestCase(null, 0, false)]
        public void TheClassificationDigitIsInEveryNoteAndResidentialMeansTwo(string classification, int digit, bool residential)
        {
            var shaped = CostPlusResultShape.Shape("ALP", new[] { Service("A", 10m), Service("B", 20m) }, classification, "02379");

            Assert.That(shaped.Slots.Select(s => s.Note), Has.All.EqualTo(digit));
            Assert.That(shaped.ClassifiedResidential, Is.EqualTo(residential));
        }

        [Test]
        public void TheSubstitutedZipIsTheZipTheRequestWasRatedFor()
        {
            Assert.That(CostPlusResultShape.Shape("ALP", new[] { Service("A", 10m) }, "Commercial", "02379").SubstitutedZip, Is.EqualTo("02379"));
        }

    }

    /// <summary>The same shaping, as it arrives in the request's row from a poll cycle (needs the FreightEstimator test database).</summary>
    public class CostPlusResultRowTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        [TestCase("ups-address-validation-residential.json", "2", true)]
        [TestCase("ups-address-validation-commercial.json", "1", false)]
        [TestCase("ups-address-validation-unknown.json", "0", false)]
        public void TheRowCarriesTheNoteDigitClassifiedResidentialAndSubstitutedZip(string addressAnswer, string note, bool residential)
        {
            QuoteCase c = Case;
            string loginId = _costPlus.Add(SandboxCostPlus.For(c));
            FakeCarrierHttp fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", addressAnswer)
                .RespondWith("rating/v2403/Shop", System.Net.HttpStatusCode.OK, c.ResponseText)
                .RespondWith("rating/v2403/Rate", System.Net.HttpStatusCode.OK, c.ResponseText);

            Assert.That(CostPlusCycle.Run(fake), Is.EqualTo(1));

            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.Null, row.ErrorDesc01);
            Assert.That(row.Slots, Is.Not.Empty);
            Assert.That(row.Slots.Select(s => s.Note), Has.All.EqualTo(note));
            Assert.That(row.ClassifiedResidential, Is.EqualTo(residential));
            Assert.That(row.SubstitutedZip, Is.EqualTo(c.ShipToZip));
        }
    }
}
