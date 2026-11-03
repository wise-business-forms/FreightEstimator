using AuthenticationServer.Models.Services;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The production charge schedule the unified Calculated Rate module reads Plant Charges through, against
    /// SANDBOX (audit finding F5). It had no tests for its carrier and column mapping, which is where F5 lived.
    /// </summary>
    public class ProductionChargeScheduleTests
    {
        // Every number differs between the UPS and GF rows, so a charge read from the wrong carrier lands on a wrong number.
        private static SandboxPlantCharges.Row[] Schedule() => new[]
        {
            new SandboxPlantCharges.Row { CarrierId = "UPS", PerPackage = 2m, PerShipment = 5m, Ground = 5m },
            new SandboxPlantCharges.Row { CarrierId = "GF",  PerPackage = 9m, PerShipment = 11m, Ground = 4m },
        };

        [Test]
        public void GroundFreightTakesItsMarkupAndBothFlatChargesFromTheGFCarrier()
        {
            using (new SandboxPlantCharges("ALP", Schedule()))
            {
                PlantServiceCharges charges = new RateCalculationsChargeSchedule().GetCharges("ALP", "UPSGroundFreight", isCwtEligible: false);

                // The live GRID2 formula takes all three from GF. The lookup used to take the flat charges from UPS (F5).
                Assert.That(charges.MarkupPercent, Is.EqualTo(4m));
                Assert.That(charges.PerPackageCharge, Is.EqualTo(9m));
                Assert.That(charges.PerShipmentCharge, Is.EqualTo(11m));
            }
        }

        [Test]
        public void UpsParcelStillTakesItsChargesFromTheUpsCarrier()
        {
            using (new SandboxPlantCharges("ALP", Schedule()))
            {
                PlantServiceCharges charges = new RateCalculationsChargeSchedule().GetCharges("ALP", "UPSGround", isCwtEligible: false);

                Assert.That(charges.MarkupPercent, Is.EqualTo(5m));
                Assert.That(charges.PerPackageCharge, Is.EqualTo(2m));
                Assert.That(charges.PerShipmentCharge, Is.EqualTo(5m));
            }
        }
    }
}
