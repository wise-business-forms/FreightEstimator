using AuthenticationServer.CostPlus;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// "No request processed twice" (#32, ADR 0005), at the same seam as the rest of the poller: one poll cycle against the
    /// FreightEstimator test database, with the recorded carrier fake. Claims are put in place as another poller instance
    /// would leave them. Assertions are on the rows FileMaker and an analyst can see: the request's result row, the claim, the Quotes.
    /// </summary>
    public class CostPlusClaimTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        private static int RunOneCycle(FakeCarrierHttp fake) => CostPlusCycle.Run(fake);

        private static int ShopCalls(FakeCarrierHttp fake) => fake.Requests.Count(r => r.Url.Contains("rating/v2403/Shop"));

        // ---- the lease ------------------------------------------------------------------------------------------

        [Test]
        public void AClaimHeldLongerThanTheLeaseIsRetakenAndTheRequestAnswered()
        {
            string loginId = _costPlus.Add(SandboxCostPlus.For(Case));
            _costPlus.AddClaim(loginId, ageSeconds: 180);   // a crashed instance took it three minutes ago and never finished
            FakeCarrierHttp fake = QuoteFlow.FakeFor(Case);

            Assert.That(RunOneCycle(fake), Is.EqualTo(1));

            Assert.That(_costPlus.ResultOf(loginId).DateRated, Is.Not.Null);
            Assert.That(_costPlus.ClaimCount(loginId), Is.EqualTo(1), "the claim is retaken, not duplicated");
            SandboxCostPlus.Claim claim = _costPlus.ClaimOf(loginId);
            Assert.That(claim.CompletedAt, Is.Not.Null);
            Assert.That(claim.QuoteId, Is.Not.Null);
            Assert.That(_costPlus.QuoteOf(claim.QuoteId.Value).Consumer, Is.EqualTo("CostPlus"));
        }

        [Test]
        public void SeveralPollersRetakingExpiredClaimsAtOnceAnswerEachRequestOnce()
        {
            const int requests = 8;
            string[] logins = Enumerable.Range(0, requests).Select(_ => _costPlus.Add(SandboxCostPlus.For(Case))).ToArray();
            foreach (string login in logins) _costPlus.AddClaim(login, ageSeconds: 180);   // every request was left by a crashed instance
            FakeCarrierHttp fake = QuoteFlow.FakeFor(Case);

            using (new SandboxPlantCharges("ALP", SandboxPlantCharges.KnownSchedule()))
            using (ServiceCatalog.Use(CostPlusCycle.ParcelServices))
            using (fake.Install())
            {
                Task[] pollers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    while (new CostPlusPollCycle().RunOnce() > 0) { }
                })).ToArray();
                Task.WaitAll(pollers);
            }

            foreach (string login in logins)
            {
                Assert.That(_costPlus.AnsweredRowCount(login), Is.EqualTo(1), "unanswered " + login);
                Assert.That(_costPlus.ClaimCount(login), Is.EqualTo(1), "claims for " + login);
                Assert.That(_costPlus.QuoteCount(login), Is.EqualTo(1), "Quotes for " + login);
            }
            Assert.That(ShopCalls(fake), Is.EqualTo(requests), "UPS was asked once per request");
        }

        [Test]
        public void AClaimWithinTheLeaseIsLeftAlone()
        {
            string loginId = _costPlus.Add(SandboxCostPlus.For(Case));
            _costPlus.AddClaim(loginId, ageSeconds: 30);    // another instance is working on it right now
            var fake = new FakeCarrierHttp();               // nothing set up: any carrier call throws

            Assert.That(RunOneCycle(fake), Is.EqualTo(0));

            Assert.That(_costPlus.ResultOf(loginId).DateRated, Is.Null);
            Assert.That(_costPlus.ClaimOf(loginId).CompletedAt, Is.Null);
            Assert.That(_costPlus.QuoteCount(loginId), Is.EqualTo(0));
            Assert.That(fake.Requests, Is.Empty);
        }

        // ---- the result write -----------------------------------------------------------------------------------

        [Test]
        public void AnAnswerAnotherInstanceWroteWhileThisOneWaitedOnUpsIsNotOverwritten()
        {
            string loginId = _costPlus.Add(SandboxCostPlus.For(Case));
            Guid otherQuote = Guid.NewGuid();
            FakeCarrierHttp fake = QuoteFlow.FakeFor(Case);
            bool answeredElsewhere = false;
            fake.Before = request =>
            {
                // This instance's claim has expired while UPS was slow; another instance retook it and answered first.
                if (answeredElsewhere || !request.Url.Contains("rating/v2403/Shop")) return;
                answeredElsewhere = true;
                _costPlus.AnswerAsAnotherInstance(loginId, otherQuote, rate: 111m);
            };

            Assert.That(RunOneCycle(fake), Is.EqualTo(0), "this instance answered nothing");

            SandboxCostPlus.Result row = _costPlus.ResultOf(loginId);
            Assert.That(row.Slots.Select(s => (s.Service, s.Rate)), Is.EqualTo(new[] { ("OtherInstance", 111m) }), "the first answer stands");
            Assert.That(_costPlus.ClaimOf(loginId).QuoteId, Is.EqualTo(otherQuote), "the claim still points at the Quote of the answer that was written");
        }

        // ---- concurrency and resubmission -----------------------------------------------------------------------

        [Test]
        public void ARequestRatedButNeverProcessedIsAnsweredOnceAndThenLeftAlone()
        {
            string loginId = _costPlus.Add(SandboxCostPlus.For(Case));
            _costPlus.MarkRatedButNotProcessed(loginId);   // the console app's fetch still counts it as not done
            FakeCarrierHttp fake = QuoteFlow.FakeFor(Case);

            Assert.That(RunOneCycle(fake), Is.EqualTo(1));
            Assert.That(_costPlus.AnsweredRowCount(loginId), Is.EqualTo(1));
            Assert.That(_costPlus.ClaimOf(loginId).CompletedAt, Is.Not.Null);

            Assert.That(RunOneCycle(fake), Is.EqualTo(0), "it is not answered again");
            Assert.That(ShopCalls(fake), Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentCyclesProduceExactlyOneResultAndOneQuotePerRequest()
        {
            const int requests = 12;
            string[] logins = Enumerable.Range(0, requests).Select(_ => _costPlus.Add(SandboxCostPlus.For(Case))).ToArray();
            FakeCarrierHttp fake = QuoteFlow.FakeFor(Case);

            using (new SandboxPlantCharges("ALP", SandboxPlantCharges.KnownSchedule()))
            using (ServiceCatalog.Use(CostPlusCycle.ParcelServices))
            using (fake.Install())
            {
                // Four pollers share the table and keep cycling until none finds anything to do.
                Task[] pollers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    while (new CostPlusPollCycle().RunOnce() > 0) { }
                })).ToArray();
                Task.WaitAll(pollers);
            }

            foreach (string login in logins)
            {
                Assert.That(_costPlus.ResultOf(login).DateRated, Is.Not.Null, "unanswered " + login);
                Assert.That(_costPlus.ClaimCount(login), Is.EqualTo(1), "claims for " + login);
                Assert.That(_costPlus.QuoteCount(login), Is.EqualTo(1), "Quotes for " + login);
            }
            Assert.That(ShopCalls(fake), Is.EqualTo(requests), "UPS was asked once per request");
        }

        [Test]
        public void AResubmissionWithANewDateRequestedIsANewRequest()
        {
            QuoteCase c = Case;
            SandboxCostPlus.Request first = SandboxCostPlus.For(c);
            first.DateRequested = DateTime.Now.AddMinutes(-10);
            string loginId = _costPlus.Add(first);
            FakeCarrierHttp fake = QuoteFlow.FakeFor(c);
            Assert.That(RunOneCycle(fake), Is.EqualTo(1));

            SandboxCostPlus.Request again = SandboxCostPlus.For(c);   // same LoginId and QtyNumber, asked again later
            again.DateRequested = DateTime.Now.AddMinutes(-1);
            _costPlus.Add(again, loginId);
            Assert.That(RunOneCycle(fake), Is.EqualTo(1));

            Assert.That(_costPlus.AnsweredRowCount(loginId), Is.EqualTo(2), "each submission has its own answer");
            Assert.That(_costPlus.ClaimCount(loginId), Is.EqualTo(2));
            Assert.That(_costPlus.QuoteCount(loginId), Is.EqualTo(2));
            Assert.That(ShopCalls(fake), Is.EqualTo(2));
        }
    }
}
