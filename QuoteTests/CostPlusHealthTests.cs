using System.Net;
using AuthenticationServer.CostPlus;
using AuthenticationServer.Models;
using FreightEstimator.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// A stalled CostPlus poller is visible (#35, ADR 0005): the two health measures and their thresholds, who is alerted and when,
    /// the health endpoint, and the probe request.
    /// </summary>
    public class CostPlusHealthTests
    {
        private DateTime _now;
        private Func<DateTime> Clock => () => _now;

        [SetUp]
        public void SetUp() => _now = new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc);

        // ---- the two measures and their thresholds ----

        [Test]
        public void HealthyWhileThePollSucceedsAndNothingWaitsTooLong()
        {
            var health = new CostPlusPollerHealth(Clock);
            health.RecordSuccessfulPoll();
            _now += TimeSpan.FromSeconds(10);

            CostPlusPollerHealth.Report report = health.Evaluate(TimeSpan.FromSeconds(30));   // exactly at the limit is still fine

            Assert.That(report.Healthy, Is.True);
            Assert.That(report.OldestUnclaimedRequestAgeSeconds, Is.EqualTo(30));
            Assert.That(report.LastSuccessfulPollUtc, Is.EqualTo(_now - TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void UnhealthyWhenTheOldestUnclaimedRequestIsOlderThan30Seconds()
        {
            var health = new CostPlusPollerHealth(Clock);
            health.RecordSuccessfulPoll();

            CostPlusPollerHealth.Report report = health.Evaluate(TimeSpan.FromSeconds(31));

            Assert.That(report.Healthy, Is.False);
            Assert.That(report.Problems, Has.Count.EqualTo(1));
        }

        [Test]
        public void UnhealthyWhenNoPollHasSucceededFor60Seconds()
        {
            var health = new CostPlusPollerHealth(Clock);
            health.RecordSuccessfulPoll();
            _now += TimeSpan.FromSeconds(61);

            Assert.That(health.Evaluate(null).Healthy, Is.False);

            health.RecordSuccessfulPoll();
            Assert.That(health.Evaluate(null).Healthy, Is.True, "a new successful poll clears it");
        }

        [Test]
        public void ApollerThatNeverPollsGoesUnhealthyCountingFromStartup()
        {
            var health = new CostPlusPollerHealth(Clock);
            _now += TimeSpan.FromSeconds(59);
            Assert.That(health.Evaluate(null).Healthy, Is.True, "still starting up");

            _now += TimeSpan.FromSeconds(2);
            CostPlusPollerHealth.Report report = health.Evaluate(null);
            Assert.That(report.Healthy, Is.False);
            Assert.That(report.LastSuccessfulPollUtc, Is.Null);
        }

        [Test]
        public void AnUnreadableRequestTableIsUnhealthy()
        {
            var health = new CostPlusPollerHealth(Clock);
            health.RecordSuccessfulPoll();
            var check = new CostPlusHealthCheck(health, () => throw new InvalidOperationException("database down"), pollerEnabled: true);

            CostPlusPollerHealth.Report report = check.Check();

            Assert.That(report.Healthy, Is.False);
            Assert.That(report.Problems, Has.Some.Contain("database down"));
        }

        // ---- alerts ----

        private class RecordingSender : IStallAlertSender
        {
            public List<string> Subjects = new List<string>();
            public bool Fail;
            public void Send(string subject, string body)
            {
                if (Fail) throw new InvalidOperationException("smtp down");
                Subjects.Add(subject);
            }
        }

        private static CostPlusPollerHealth.Report Unhealthy => new CostPlusPollerHealth.Report { Healthy = false, Problems = new List<string> { "stalled" } };
        private static CostPlusPollerHealth.Report Healthy => new CostPlusPollerHealth.Report { Healthy = true, Problems = new List<string>() };

        [Test]
        public void AStallAlertsOnceThenRemindsThenSaysItRecovered()
        {
            var sender = new RecordingSender();
            var alerts = new CostPlusStallAlerts(sender, Clock);

            alerts.Observe(Healthy);
            Assert.That(sender.Subjects, Is.Empty);

            alerts.Observe(Unhealthy);
            _now += TimeSpan.FromSeconds(10);
            alerts.Observe(Unhealthy);
            Assert.That(sender.Subjects, Has.Count.EqualTo(1), "one alert, not one per check");

            _now += CostPlusStallAlerts.ReminderEvery;
            alerts.Observe(Unhealthy);
            Assert.That(sender.Subjects, Has.Count.EqualTo(2), "a reminder while it stays down");

            alerts.Observe(Healthy);
            Assert.That(sender.Subjects, Has.Count.EqualTo(3));
            Assert.That(sender.Subjects.Last(), Does.Contain("recovered"));

            alerts.Observe(Healthy);
            Assert.That(sender.Subjects, Has.Count.EqualTo(3));
        }

        [Test]
        public void AFailedSendIsTriedAgainAtTheNextCheck()
        {
            var sender = new RecordingSender { Fail = true };
            var alerts = new CostPlusStallAlerts(sender, Clock);

            Assert.Throws<InvalidOperationException>(() => alerts.Observe(Unhealthy));
            sender.Fail = false;
            alerts.Observe(Unhealthy);

            Assert.That(sender.Subjects, Has.Count.EqualTo(1));
        }

        // ---- the probe ----

        [Test]
        public void QuietHoursMayRunPastMidnight()
        {
            TimeSpan start = TimeSpan.FromHours(22), end = TimeSpan.FromHours(5);

            Assert.That(CostPlusProbe.IsQuietHour(TimeSpan.FromHours(23), start, end), Is.True);
            Assert.That(CostPlusProbe.IsQuietHour(TimeSpan.FromHours(2), start, end), Is.True);
            Assert.That(CostPlusProbe.IsQuietHour(TimeSpan.FromHours(5), start, end), Is.False);
            Assert.That(CostPlusProbe.IsQuietHour(TimeSpan.FromHours(12), start, end), Is.False);
            Assert.That(CostPlusProbe.IsQuietHour(TimeSpan.FromHours(3), TimeSpan.FromHours(1), TimeSpan.FromHours(4)), Is.True);
        }

        [Test]
        public void AProbeIsAnsweredWithoutAQuoteAnyCarrierCallOrAPlant()
        {
            using (var costPlus = new SandboxCostPlus())
            {
                string loginId = costPlus.Add(new SandboxCostPlus.Request { FromState = "ZZ", ToState = "MA", ToZip = "02379" }, CostPlusProbe.LoginIdPrefix + Guid.NewGuid().ToString("N"));
                var fake = new FakeCarrierHttp();   // nothing set up: any carrier call throws

                int answered = CostPlusCycle.Run(fake);

                Assert.That(answered, Is.EqualTo(1));
                Assert.That(fake.Requests, Is.Empty, "a probe must never reach UPS");
                SandboxCostPlus.Result row = costPlus.ResultOf(loginId);
                Assert.That(row.DateRated, Is.Not.Null);
                Assert.That(row.DateProcessed, Is.Not.Null);
                Assert.That(row.ErrorCode01, Is.Null, row.ErrorDesc01);
                Assert.That(costPlus.ClaimOf(loginId).CompletedAt, Is.Not.Null);
                Assert.That(costPlus.ClaimOf(loginId).QuoteId, Is.Null, "no Quote, so nothing for reporting to see");
                Assert.That(costPlus.QuoteCount(loginId), Is.EqualTo(0));
            }
        }

        [Test]
        public void AnUnansweredProbeShowsUpAsAnOldUnclaimedRequest()
        {
            using (var costPlus = new SandboxCostPlus())
            {
                costPlus.Add(new SandboxCostPlus.Request { ToState = "MA", ToZip = "02379", DateRequested = DateTime.Now.AddSeconds(-45) }, CostPlusProbe.LoginIdPrefix + Guid.NewGuid().ToString("N"));

                TimeSpan? age = CostPlusQueue.OldestUnclaimedAge();

                Assert.That(age, Is.Not.Null);
                Assert.That(age.Value, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(44)));
                Assert.That(new CostPlusPollerHealth().Evaluate(age).Healthy, Is.False);
            }
        }

        // ---- the endpoint ----

        private static WebApplicationFactory<Program> App(bool pollerEnabled) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string> { ["CostPlus:Poller:Enabled"] = pollerEnabled ? "true" : "false", ["CostPlus:Probe:Enabled"] = "false" }));
            });

        [Test]
        public async Task TheEndpointSaysDisabledWhenThePollerIsOff()
        {
            try
            {
                using (var app = App(pollerEnabled: false))
                {
                    HttpResponseMessage response = await app.CreateClient().GetAsync("/health/costplus-poller");

                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("disabled"));
                }
            }
            finally { Configuration.ConnectionStringSource = null; }
        }

        [Test]
        public async Task TheEndpointReportsBothMeasuresAndItsStatusMatchesTheVerdict()
        {
            try
            {
                using (var app = App(pollerEnabled: true))
                {
                    HttpClient client = app.CreateClient();
                    HttpResponseMessage response = await client.GetAsync("/health/costplus-poller");
                    string body = await response.Content.ReadAsStringAsync();

                    Assert.That(body, Does.Contain("oldestUnclaimedRequestAgeSeconds"));
                    Assert.That(body, Does.Contain("lastSuccessfulPollUtc"));
                    Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable));
                    Assert.That(response.StatusCode == HttpStatusCode.OK, Is.EqualTo(body.Contains("\"status\":\"healthy\"")));
                }
            }
            finally { Configuration.ConnectionStringSource = null; }
        }
    }
}
