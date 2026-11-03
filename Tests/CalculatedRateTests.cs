using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.Tests
{
    public class CalculatedRateTests
    {
        [Test]
        public void AppliesMarkupPerPackageAndPerShipmentCharges()
        {
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule()
                .With("ALP", "UPSGround", isCwtEligible: false, markupPercent: 10m, perPackageCharge: 2m, perShipmentCharge: 5m);
            RatingContext context = new RatingContext { PlantCode = "ALP", ServiceName = "UPSGround", PackageCount = 3, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(100m, chargeSchedule, context);

            // 100 + 100*0.10 + 2*3 + 5 = 121
            Assert.That(result.Amount, Is.EqualTo(121m));
            Assert.That(result.CarrierRate, Is.EqualTo(100m));
            Assert.That(result.MarkupPercent, Is.EqualTo(10m));
            Assert.That(result.PerPackageCharge, Is.EqualTo(2m));
            Assert.That(result.PerShipmentCharge, Is.EqualTo(5m));
        }

        [Test]
        public void AppliesMarkupToCwtEligibleShipmentsJustAsToNonCwtShipments()
        {
            // Regression test for GRID1 single-plant's bug: markup used to only apply when CWT-eligible.
            // Here it's the CWT-eligible shipment that must still get marked up.
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule()
                .With("ALP", "UPSGround", isCwtEligible: true, markupPercent: 8m, perPackageCharge: 1m, perShipmentCharge: 3m);
            RatingContext context = new RatingContext { PlantCode = "ALP", ServiceName = "UPSGround", PackageCount = 2, IsCwtEligible = true };

            CalculatedRate result = CalculatedRate.Apply(200m, chargeSchedule, context);

            // 200 + 200*0.08 + 1*2 + 3 = 221
            Assert.That(result.Amount, Is.EqualTo(221m));
        }

        [Test]
        public void AppliesMarkupRegardlessOfServiceName()
        {
            // Regression test for GRID2's bug: markup used to only ever be populated for "UPSGroundFreight".
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule()
                .With("FTW", "UPSSaver", isCwtEligible: false, markupPercent: 12m, perPackageCharge: 0m, perShipmentCharge: 0m);
            RatingContext context = new RatingContext { PlantCode = "FTW", ServiceName = "UPSSaver", PackageCount = 1, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(75m, chargeSchedule, context);

            // 75 + 75*0.12 = 84
            Assert.That(result.Amount, Is.EqualTo(84m));
        }

        [Test]
        public void FallsBackToZeroChargesWhenScheduleHasNoEntry()
        {
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule();
            RatingContext context = new RatingContext { PlantCode = "ALP", ServiceName = "UPSSaver", PackageCount = 1, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(50m, chargeSchedule, context);

            Assert.That(result.Amount, Is.EqualTo(50m));
            Assert.That(result.MarkupPercent, Is.EqualTo(0m));
        }

        [Test]
        public void MultipliesPerPackageChargeByPackageCount()
        {
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule()
                .With("ALP", "UPSGround", isCwtEligible: false, markupPercent: 0m, perPackageCharge: 4m, perShipmentCharge: 10m);
            RatingContext context = new RatingContext { PlantCode = "ALP", ServiceName = "UPSGround", PackageCount = 5, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(60m, chargeSchedule, context);

            // 60 + 0 + 4*5 + 10 = 90
            Assert.That(result.Amount, Is.EqualTo(90m));
        }

        [Test]
        public void ReadsChargesForTheGivenPlantAndServiceOnly()
        {
            // Guards against accidentally reading the wrong plant's or service's charges.
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule()
                .With("ALP", "UPSGround", isCwtEligible: false, markupPercent: 50m, perPackageCharge: 99m, perShipmentCharge: 99m)
                .With("FTW", "UPSGround", isCwtEligible: false, markupPercent: 5m, perPackageCharge: 1m, perShipmentCharge: 2m);
            RatingContext context = new RatingContext { PlantCode = "FTW", ServiceName = "UPSGround", PackageCount = 1, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(100m, chargeSchedule, context);

            // 100 + 100*0.05 + 1*1 + 2 = 108, not the ALP plant's numbers
            Assert.That(result.Amount, Is.EqualTo(108m));
        }

        // The rounding rule signed off in #13 (docs/adr/0004): half-up to the cent, half away from zero, applied
        // once, to the final Calculated Rate only. Components stay unrounded.
        [TestCase("10.005", "10.01")]
        [TestCase("10.004", "10.00")]
        [TestCase("10.015", "10.02")]
        [TestCase("-10.005", "-10.01")]
        public void RoundsTheFinalAmountHalfAwayFromZeroToTheCent(string carrierRate, string expected)
        {
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule();
            RatingContext context = new RatingContext { PlantCode = "ALP", ServiceName = "UPSGroundFreight", PackageCount = 1, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(decimal.Parse(carrierRate), chargeSchedule, context);

            Assert.That(result.RoundedAmount, Is.EqualTo(decimal.Parse(expected)));
        }

        [Test]
        public void RoundingLeavesTheUnroundedAmountAndTheComponentsAlone()
        {
            IChargeSchedule chargeSchedule = new InMemoryChargeSchedule()
                .With("ALP", "UPSGroundFreight", isCwtEligible: false, markupPercent: 3.333m, perPackageCharge: 1.115m, perShipmentCharge: 0.0049m);
            RatingContext context = new RatingContext { PlantCode = "ALP", ServiceName = "UPSGroundFreight", PackageCount = 2, IsCwtEligible = false };

            CalculatedRate result = CalculatedRate.Apply(100m, chargeSchedule, context);

            // 100 + 100*0.03333 + 1.115*2 + 0.0049 = 105.5679
            Assert.That(result.Amount, Is.EqualTo(105.5679m));
            Assert.That(result.RoundedAmount, Is.EqualTo(105.57m));
            Assert.That(result.MarkupPercent, Is.EqualTo(3.333m));
            Assert.That(result.PerPackageCharge, Is.EqualTo(1.115m));
            Assert.That(result.PerShipmentCharge, Is.EqualTo(0.0049m));
        }
    }
}
