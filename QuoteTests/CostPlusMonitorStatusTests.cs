using AuthenticationServer.CostPlus;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// How the Poller Monitor judges what the poller instances last reported (#50): which are running, whether any is, and
    /// whether the poller is healthy by the same limits as the health check (no poll for 60 s, oldest unclaimed over 30 s).
    /// Pure: the ages come in, the verdict goes out.
    /// </summary>
    public class CostPlusMonitorStatusTests
    {
        private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

        private static PollerInstanceBeat Beat(string id = "ALPWEB:1", string mode = "Live", double heartbeatAge = 3, double? sincePoll = 1, double startedAgo = 600) =>
            new PollerInstanceBeat
            {
                InstanceId = id, Machine = id.Split(':')[0], Mode = mode,
                HeartbeatAge = S(heartbeatAge), SinceLastSuccessfulPoll = sincePoll == null ? (TimeSpan?)null : S(sincePoll.Value), StartedAgo = S(startedAgo)
            };

        [Test]
        public void NoHeartbeatsMeansNoPollerRunning()
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new PollerInstanceBeat[0], null);

            Assert.That(status.AnyRunning, Is.False);
            Assert.That(status.Healthy, Is.False);
            Assert.That(status.Problems, Has.Some.Contain("No poller is running"));
            Assert.That(status.Instances, Is.Empty);
        }

        [Test]
        public void AFreshHeartbeatIsRunningAndHealthy()
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { Beat() }, null);

            Assert.That(status.AnyRunning, Is.True);
            Assert.That(status.Healthy, Is.True);
            Assert.That(status.Problems, Is.Empty);
            Assert.That(status.Instances.Single().Running, Is.True);
            Assert.That(status.Instances.Single().Mode, Is.EqualTo("Live"));
        }

        [TestCase(29.0, true)]
        [TestCase(31.0, false)]
        public void AHeartbeatOlderThanThirtySecondsIsAStoppedInstance(double heartbeatAge, bool running)
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { Beat(heartbeatAge: heartbeatAge) }, null);

            Assert.That(status.Instances.Single().Running, Is.EqualTo(running));
            Assert.That(status.AnyRunning, Is.EqualTo(running));
            if (!running) Assert.That(status.Problems, Has.Some.Contain("No poller is running"));
        }

        [Test]
        public void TwoOverlappingInstancesAreBothShown_AndAStoppedOneDoesNotHideTheLiveOne()
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[]
            {
                Beat("ALPWEB:1", heartbeatAge: 120),   // the old process, gone after a recycle
                Beat("ALPWEB:2", heartbeatAge: 2),
                Beat("ALPWEB:3", heartbeatAge: 4)      // an overlapping new one
            }, null);

            Assert.That(status.Instances.Select(i => i.InstanceId), Is.EquivalentTo(new[] { "ALPWEB:1", "ALPWEB:2", "ALPWEB:3" }));
            Assert.That(status.Instances.Where(i => i.Running).Select(i => i.InstanceId), Is.EquivalentTo(new[] { "ALPWEB:2", "ALPWEB:3" }));
            Assert.That(status.Healthy, Is.True);
            Assert.That(status.Instances.First().Running, Is.True, "running instances are listed first");
        }

        [TestCase(59.0, true)]
        [TestCase(61.0, false)]
        public void HealthFollowsTheSixtySecondPollLimit(double sincePoll, bool healthy)
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { Beat(sincePoll: sincePoll) }, null);

            Assert.That(status.Healthy, Is.EqualTo(healthy));
            if (!healthy) Assert.That(status.Problems, Has.Some.Contain("No poll has succeeded for"));
        }

        [Test]
        public void TheFreshestPollAmongRunningInstancesCounts()
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { Beat("A:1", sincePoll: 300), Beat("A:2", sincePoll: 2) }, null);

            Assert.That(status.Healthy, Is.True, "one instance is polling, so the poller is");
        }

        [Test]
        public void AnInstanceThatNeverPolledIsJudgedFromItsStartup()
        {
            Assert.That(CostPlusMonitorStatus.Judge(new[] { Beat(sincePoll: null, startedAgo: 20) }, null).Healthy, Is.True);
            Assert.That(CostPlusMonitorStatus.Judge(new[] { Beat(sincePoll: null, startedAgo: 90) }, null).Healthy, Is.False);
        }

        [TestCase(29.0, true)]
        [TestCase(31.0, false)]
        public void InLiveModeTheOldestUnclaimedRequestFollowsTheThirtySecondLimit(double oldestUnclaimed, bool healthy)
        {
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { Beat(mode: "Live") }, S(oldestUnclaimed));

            Assert.That(status.Healthy, Is.EqualTo(healthy));
            if (!healthy) Assert.That(status.Problems, Has.Some.Contain("oldest unclaimed request"));
        }

        [Test]
        public void InShadowModeTheWaitingRequestsSayNothingAboutThePoller()
        {
            // The console app owns the queue in Shadow mode, so how long a request has waited is not the poller's fault.
            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { Beat(mode: "Shadow") }, S(500));

            Assert.That(status.Healthy, Is.True);
        }

        [Test]
        public void TheLatestAlertAndProbeAcrossInstancesAreReported()
        {
            PollerInstanceBeat a = Beat("A:1"); a.LastAlertSentAgo = S(900); a.LastProbeInsertedAgo = S(200);
            PollerInstanceBeat b = Beat("A:2"); b.LastAlertSentAgo = S(60);
            PollerInstanceBeat c = Beat("A:3");

            CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(new[] { a, b, c }, null);

            Assert.That(status.LastAlertSentAgo, Is.EqualTo(S(60)));
            Assert.That(status.LastProbeInsertedAgo, Is.EqualTo(S(200)));
            Assert.That(CostPlusMonitorStatus.Judge(new[] { c }, null).LastAlertSentAgo, Is.Null);
        }
    }
}
