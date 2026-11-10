using AuthenticationServer.CostPlus;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The waiting queue panel on the Poller Monitor (#49): how many CostPlus requests are waiting, the oldest ten, and what is in
    /// flight, read from the request table with the same "waiting" rule the poll cycle and the health check use. Read-only.
    /// Each test makes its own requests (SandboxCostPlus) far older than anything else in the test database, so they come first.
    /// </summary>
    public class CostPlusQueueSnapshotTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        /// <summary>A request <paramref name="daysAgo"/> days old (and so older than any other request in the table).</summary>
        private string Old(int daysAgo, Action<SandboxCostPlus.Request> change = null, string loginId = null)
        {
            var r = new SandboxCostPlus.Request { FromState = "GA", ToState = "GA", ToZip = "30004", NumPackages = 1, PkgWeight = 5, LastPkgWeight = 5, DateRequested = DateTime.Now.AddDays(-daysAgo) };
            change?.Invoke(r);
            return loginId == null ? _costPlus.Add(r) : _costPlus.Add(r, loginId);
        }

        private static QueueRequest Find(IEnumerable<QueueRequest> list, string loginId) => list.SingleOrDefault(q => q.LoginId == loginId);

        [Test]
        public void WaitingCountsRequestsNobodyHasClaimedOrAnswered()
        {
            int before = CostPlusQueue.Snapshot().Waiting;
            Old(400); Old(401); Old(402);

            Assert.That(CostPlusQueue.Snapshot().Waiting, Is.EqualTo(before + 3));
        }

        [Test]
        public void TheOldestTenAreListedOldestFirst_WithTheRequestsInputsButNoStreetAddress()
        {
            string[] ids = Enumerable.Range(0, 12).Select(i => Old(500 + i, r => { r.FromState = "PA"; r.ToZip = "1520" + (i % 10); })).ToArray();

            QueueSnapshot snapshot = CostPlusQueue.Snapshot();

            Assert.That(snapshot.Oldest, Has.Count.EqualTo(10));
            Assert.That(snapshot.Oldest.Select(q => q.LoginId), Is.EqualTo(Enumerable.Reverse(ids).Take(10)), "oldest first");
            Assert.That(snapshot.Oldest.Select(q => q.AgeSeconds), Is.Ordered.Descending);
            QueueRequest oldest = snapshot.Oldest[0];
            Assert.That(oldest.AgeSeconds, Is.GreaterThan(511L * 24 * 3600 - 60));
            Assert.That((oldest.FromState, oldest.ToZip, oldest.Ups, oldest.Ltl, oldest.QtyNumber), Is.EqualTo(("PA", "15201", true, false, 1)));
            Assert.That(typeof(QueueRequest).GetFields().Select(f => f.Name), Has.None.Contains("Address"), "street addresses are never read");
        }

        [Test]
        public void ARequestAnotherInstanceHasClaimedIsNotWaiting_ButAnExpiredClaimIs()
        {
            string live = Old(400), expired = Old(401);
            _costPlus.AddClaim(live, ageSeconds: 30);        // inside the 2-minute lease
            _costPlus.AddClaim(expired, ageSeconds: 180);    // a crashed instance's: the request waits again

            QueueSnapshot snapshot = CostPlusQueue.Snapshot();

            Assert.That(Find(snapshot.Oldest, expired), Is.Not.Null);
            Assert.That(Find(snapshot.Oldest, live), Is.Null);
        }

        [Test]
        public void ARequestUnderALiveClaimIsInFlight_WithWhoHoldsIt()
        {
            string live = Old(400), expired = Old(401), waiting = Old(402);
            _costPlus.AddClaim(live, ageSeconds: 30);
            _costPlus.AddClaim(expired, ageSeconds: 180);

            QueueSnapshot snapshot = CostPlusQueue.Snapshot();

            QueueRequest inFlight = Find(snapshot.InFlight, live);
            Assert.That(inFlight, Is.Not.Null);
            Assert.That(inFlight.ClaimedBy, Is.EqualTo("another-instance"));
            Assert.That(Find(snapshot.InFlight, expired), Is.Null, "an expired claim is no longer in flight");
            Assert.That(Find(snapshot.InFlight, waiting), Is.Null);
        }

        [Test]
        public void AnAnsweredRequestAndAnLtlRequestAreNotWaitingForThePoller()
        {
            int before = CostPlusQueue.Snapshot().Waiting;
            string answered = Old(400), ltl = Old(401, r => r.RequestLtl = true), ltlToo = Old(402, r => { r.RequestUps = true; r.RequestLtl = true; });
            _costPlus.MarkAnswered(answered);

            QueueSnapshot snapshot = CostPlusQueue.Snapshot();

            Assert.That(snapshot.Waiting, Is.EqualTo(before));
            Assert.That(snapshot.Oldest.Select(q => q.LoginId), Has.None.AnyOf(answered, ltl, ltlToo));
        }

        [Test]
        public void ProbeRequestsAreFlagged_AndTheLatestProbeSaysWhetherItWasAnswered()
        {
            string probe = Old(0, r => r.DateRequested = DateTime.Now.AddSeconds(-90), CostPlusProbe.LoginIdPrefix + "snapshot-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            QueueSnapshot waiting = CostPlusQueue.Snapshot();

            Assert.That(waiting.LatestProbe, Is.Not.Null);
            Assert.That(waiting.LatestProbe.SentAgeSeconds, Is.InRange(85, 120));
            Assert.That(waiting.LatestProbe.Answered, Is.False);
            Assert.That(waiting.Oldest.Concat(waiting.InFlight).Where(q => q.LoginId == probe).All(q => q.IsProbe), Is.True);

            _costPlus.MarkAnswered(probe);

            Assert.That(CostPlusQueue.Snapshot().LatestProbe.Answered, Is.True);
        }

        [Test]
        public void WithNoProbeThereIsNoLatestProbe()
        {
            // The table may hold probes from other work; this only checks the shape when one is absent.
            if (CostPlusQueue.Snapshot().LatestProbe != null) Assert.Inconclusive("the test database has a probe request in it");
            Assert.That(CostPlusQueue.Snapshot().LatestProbe, Is.Null);
        }
    }
}
