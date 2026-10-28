namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Plant AND is the one plant whose production Plant Charges carry a markup on non-CWT quotes (11.5% on
    /// Ground and 3 Day Select, plus $2.00 per package). The GMs decided in #14 that it must apply. GRID 1
    /// does not apply it today, so these pin today's numbers; the cutover to the unified formula (#20) will
    /// change exactly these, by the amounts in the comments, and then the expected values move with it.
    ///
    /// Case 444349: AND, 1 package of 15 lb, so non-CWT (under 200 lb). Published rates from the recorded UPS
    /// response: Ground 32.00, 3 Day Select 74.16.
    /// </summary>
    public class AndNonCwtMarkupTests
    {
        [Test]
        public void Grid1_DoesNotYetApplyAndsConfiguredMarkupToNonCwtGroundOr3DaySelect()
        {
            QuoteCase c = QuoteCase.All("parcel").First(x => x.CaseId == "444349");

            var andCharges = new SandboxPlantCharges.Row
            {
                CarrierId = "UPS", PerPackage = 2.00m, PerShipment = 0m,
                Ground = 11.5m, ThreeDaySelect = 11.5m
            };
            var quoted = QuoteFlow.Run(c, schedule: new[] { andCharges });

            // Today: published + $2.00 per package, no markup.
            //   Ground         32.00 + 2.00           = 34.00
            //   3 Day Select   74.16 + 2.00           = 76.16
            // After #14 is applied (published x 1.115 + $2.00):
            //   Ground         32.00 x 1.115 + 2.00   = 37.68
            //   3 Day Select   74.16 x 1.115 + 2.00   = 84.6884
            Assert.That((decimal)quoted["UPSGround"]["calculatedRate"], Is.EqualTo(34.00m));
            Assert.That((decimal)quoted["UPS3DaySelect"]["calculatedRate"], Is.EqualTo(76.16m));
        }
    }
}
