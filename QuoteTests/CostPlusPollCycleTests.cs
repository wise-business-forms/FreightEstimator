using AuthenticationServer.CostPlus;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The CostPlus poller (#31, ADR 0005) at its one seam: a single poll cycle against the FreightEstimator test database.
    /// Requests are inserted in CostPlus's layout; the carriers are the recorded-response fake (anything it was not told
    /// about throws, so nothing reaches UPS). Assertions are on what FileMaker and an analyst could see afterwards: the
    /// request's result row, the claim, and the logged Quote.
    /// </summary>
    public class CostPlusPollCycleTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        /// <summary>The first recorded UPS parcel case: 6 packages of 19 lb to MA 02379. UPS's recorded answer is plant-independent.</summary>
        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        private static void RunOneCycle(FakeCarrierHttp fake) => CostPlusCycle.Run(fake);

        [Test]
        public void AUpsOnlyRequestFromGeorgiaIsAnsweredInItsRowAndItsQuoteIsLogged()
        {
            QuoteCase c = Case;
            string loginId = _costPlus.Add(SandboxCostPlus.For(c));
            FakeCarrierHttp fake = QuoteFlow.FakeFor(c);

            RunOneCycle(fake);

            // The answer is in the request's own row, as FileMaker reads it.
            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.ErrorCode01, Is.Null, row.ErrorDesc01);
            Assert.That(row.DateProcessed, Is.Not.Null);
            Assert.That(row.DateRated, Is.Not.Null);
            Assert.That(row.Slots, Is.Not.Empty);
            Assert.That(row.Slots.Select(s => s.Carrier), Has.All.EqualTo("UPS"));
            Assert.That(row.Slots.Select(s => s.Rate), Is.Ordered, "cheapest first");

            // Worked examples from the recorded UPS answer and the known Plant Charges (SandboxPlantCharges.KnownSchedule):
            //   UPS3DaySelect, not CWT:  published 312.30 + 6% markup 18.738 + 6 packages x 2.00 + 5.00 = 348.038 -> 348.04
            //   UPS2ndDayAir, CWT:       negotiated 192.86 + 11% markup 21.2146 + 6 packages x 1.00 + 3.00 = 223.0746 -> 223.07
            Assert.That(row.Slots.Single(s => s.Service == "UPS3DaySelect").Rate, Is.EqualTo(348.04m));
            Assert.That(row.Slots.Single(s => s.Service == "UPS2ndDayAir").Rate, Is.EqualTo(223.07m));

            // The request was claimed, and the Quote is logged against the fixed CostPlus Consumer and the ALP Plant.
            SandboxCostPlus.Claim claim = _costPlus.ClaimOf(loginId);
            Assert.That(claim, Is.Not.Null, "no claim was taken");
            Assert.That(claim.CompletedAt, Is.Not.Null);
            Assert.That(claim.CompletedAt, Is.GreaterThanOrEqualTo(claim.ClaimedAt), "timing");
            Assert.That(claim.QuoteId, Is.Not.Null);

            SandboxCostPlus.QuoteRow quote = _costPlus.QuoteOf(claim.QuoteId.Value);
            Assert.That(quote, Is.Not.Null, "no Quote was logged");
            Assert.That(quote.Consumer, Is.EqualTo("CostPlus"));
            Assert.That(quote.PlantCode, Is.EqualTo("ALP"));
            Assert.That(int.TryParse(quote.ReferenceNumber, out _), Is.True, "the generated Reference Number is an integer");

            // Only UPS was asked, and only for the rating it needs.
            Assert.That(fake.Requests.Select(r => r.Url), Has.All.Contain("ups.com"));
        }

        [Test]
        public void OnlyRowsThatAskForUpsAndNotLtlAreClaimed()
        {
            QuoteCase c = Case;
            SandboxCostPlus.Request upsAndLtl = SandboxCostPlus.For(c); upsAndLtl.RequestUps = true; upsAndLtl.RequestLtl = true;
            SandboxCostPlus.Request ltlOnly = SandboxCostPlus.For(c); ltlOnly.RequestUps = false; ltlOnly.RequestLtl = true;
            SandboxCostPlus.Request neither = SandboxCostPlus.For(c); neither.RequestUps = false; neither.RequestLtl = false;
            string[] logins = { _costPlus.Add(upsAndLtl), _costPlus.Add(ltlOnly), _costPlus.Add(neither) };
            var fake = new FakeCarrierHttp();   // nothing set up: any carrier call throws

            using (fake.Install())
            {
                Assert.That(new CostPlusPollCycle().RunOnce(), Is.EqualTo(0));
            }

            foreach (string login in logins)
            {
                Assert.That(_costPlus.ClaimOf(login), Is.Null, "claimed " + login);
                Assert.That(_costPlus.ResultOf(login).DateRated, Is.Null, "answered " + login);
            }
            Assert.That(fake.Requests, Is.Empty);
        }

        [Test]
        public void ALtlFlagThatIsNullCountsAsUnset()
        {
            QuoteCase c = Case;
            SandboxCostPlus.Request request = SandboxCostPlus.For(c);
            request.RequestLtl = null;
            string loginId = _costPlus.Add(request);

            RunOneCycle(QuoteFlow.FakeFor(c));

            Assert.That(_costPlus.ResultOf(loginId).DateRated, Is.Not.Null);
        }
    }
}
