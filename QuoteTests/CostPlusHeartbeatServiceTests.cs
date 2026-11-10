using AuthenticationServer.CostPlus;
using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Each CostPlus Poller instance reports a heartbeat about every 10 s (#50). The report is best-effort: whatever goes wrong
    /// with it, the poller carries on answering requests.
    /// </summary>
    public class CostPlusHeartbeatServiceTests
    {
        private DateTime _now;
        private Func<DateTime> Clock => () => _now;

        [SetUp]
        public void SetUp() => _now = new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc);

        private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

        private class CapturingStore : IPollerHeartbeatStore
        {
            public readonly List<PollerBeat> Written = new List<PollerBeat>();
            public void Write(PollerBeat beat) => Written.Add(beat);
            public List<PollerInstanceBeat> ReadRecent() => new List<PollerInstanceBeat>();
        }

        private class FailingStore : IPollerHeartbeatStore
        {
            public int Attempts;
            public void Write(PollerBeat beat) { Attempts++; throw new InvalidOperationException("the database is down"); }
            public List<PollerInstanceBeat> ReadRecent() => throw new InvalidOperationException("the database is down");
        }

        private static IConfiguration Config(string mode) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["CostPlus:Poller:Enabled"] = "true", ["CostPlus:Poller:Mode"] = mode }).Build();

        // ---- what the poller counts and reports ----

        [Test]
        public void TheBeatCarriesTheCountersAndHowLongAgoThingsHappened()
        {
            var health = new CostPlusPollerHealth(Clock);
            health.RecordIdlePoll(); health.RecordIdlePoll();
            health.RecordRequestsHandled(1);
            health.RecordSuccessfulPoll();
            health.RecordAlertSent();
            health.RecordProbeInserted();
            _now += S(5);

            PollerBeat beat = health.ToBeat("ALPWEB:7", "ALPWEB", "Live");

            Assert.That((beat.InstanceId, beat.Machine, beat.Mode), Is.EqualTo(("ALPWEB:7", "ALPWEB", "Live")));
            Assert.That((beat.IdlePolls, beat.RequestsHandled, beat.DroppedEvents), Is.EqualTo((2L, 1L, 0L)));
            Assert.That(beat.SinceLastSuccessfulPoll, Is.EqualTo(S(5)));
            Assert.That(beat.LastAlertSentAgo, Is.EqualTo(S(5)));
            Assert.That(beat.LastProbeInsertedAgo, Is.EqualTo(S(5)));
            Assert.That(beat.Uptime, Is.EqualTo(S(5)));
        }

        [Test]
        public void BeforeAnythingHappensThereAreNoAgesToReport()
        {
            var health = new CostPlusPollerHealth(Clock);

            PollerBeat beat = health.ToBeat("ALPWEB:7", "ALPWEB", "Shadow");

            Assert.That(beat.SinceLastSuccessfulPoll, Is.Null);
            Assert.That(beat.LastAlertSentAgo, Is.Null);
            Assert.That(beat.LastProbeInsertedAgo, Is.Null);
        }

        [Test]
        public void TheStallMonitorSaysWhetherItSentAnAlert()
        {
            var sent = new List<string>();
            var sender = new RecordingSender(sent);
            var alerts = new CostPlusStallAlerts(sender, Clock);
            var healthy = new CostPlusPollerHealth.Report { Healthy = true, Problems = new List<string>() };
            var stalled = new CostPlusPollerHealth.Report { Healthy = false, Problems = new List<string> { "stalled" } };

            Assert.That(alerts.Observe(healthy), Is.False, "nothing wrong, nothing sent");
            Assert.That(alerts.Observe(stalled), Is.True, "the first stalled check alerts");
            Assert.That(alerts.Observe(stalled), Is.False, "the next one is within the reminder interval");
            Assert.That(alerts.Observe(healthy), Is.True, "recovery is alerted too");
        }

        private class RecordingSender : IStallAlertSender
        {
            private readonly List<string> _sent;
            public RecordingSender(List<string> sent) => _sent = sent;
            public void Send(string subject, string body) => _sent.Add(subject);
        }

        // ---- the heartbeat service ----

        [TestCase("Shadow")]
        [TestCase("Live")]
        public void TheServiceWritesTheInstanceWithItsModeAndTheCountersSoFar(string mode)
        {
            var store = new CapturingStore();
            var health = new CostPlusPollerHealth(Clock);
            health.RecordRequestsHandled(3);
            var service = new CostPlusHeartbeatService(Config(mode), NullLogger<CostPlusHeartbeatService>.Instance, health, store);

            service.WriteOnce();

            PollerBeat beat = store.Written.Single();
            Assert.That(beat.Mode, Is.EqualTo(mode));
            Assert.That(beat.Machine, Is.EqualTo(Environment.MachineName));
            Assert.That(beat.InstanceId, Is.EqualTo(Environment.MachineName + ":" + Environment.ProcessId));
            Assert.That(beat.RequestsHandled, Is.EqualTo(3));
        }

        [Test]
        public void AHeartbeatThatCannotBeWrittenIsSwallowed_AndTheNextOneIsStillTried()
        {
            var store = new FailingStore();
            var service = new CostPlusHeartbeatService(Config("Live"), NullLogger<CostPlusHeartbeatService>.Instance, new CostPlusPollerHealth(Clock), store);

            Assert.DoesNotThrow(service.WriteOnce);
            Assert.DoesNotThrow(service.WriteOnce);

            Assert.That(store.Attempts, Is.EqualTo(2));
        }

        // ---- inside the running app ----

        private SandboxCostPlus _costPlus;

        [TearDown]
        public void TearDown()
        {
            _costPlus?.Dispose();
            Configuration.ConnectionStringSource = null;   // the app sets it at startup; a later test must not see a disposed host's
            if (_cleanUpHeartbeat) DeleteHeartbeat(Environment.MachineName + ":" + Environment.ProcessId);
        }

        private bool _cleanUpHeartbeat;

        private static void DeleteHeartbeat(string instanceId)
        {
            using (var conn = new System.Data.SqlClient.SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["FreightEstimator"].ConnectionString))
            {
                conn.Open();
                using (var cmd = new System.Data.SqlClient.SqlCommand("DELETE FROM dbo.CostPlusPollerHeartbeat WHERE InstanceId = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", instanceId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        private static WebApplicationFactory<Program> App(IPollerHeartbeatStore store = null) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string> { ["CostPlus:Poller:Enabled"] = "true", ["CostPlus:Poller:Mode"] = "Live" }));
                if (store != null) b.ConfigureTestServices(services => services.AddSingleton(store));
            });

        private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            DateTime until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                if (condition()) return true;
                Thread.Sleep(100);
            }
            return condition();
        }

        [Test]
        public void ThePollerStillAnswersRequestsWhileEveryHeartbeatFails()
        {
            _costPlus = new SandboxCostPlus();
            QuoteCase c = Case;
            string loginId = _costPlus.Add(SandboxCostPlus.For(c));
            FakeCarrierHttp fake = QuoteFlow.FakeFor(c);
            var store = new FailingStore();

            using (new SandboxPlantCharges("ALP", SandboxPlantCharges.KnownSchedule()))
            using (ServiceCatalog.Use(CostPlusCycle.ParcelServices))
            using (fake.Install())
            using (WebApplicationFactory<Program> app = App(store))
            {
                app.CreateClient();   // starts the host, and with it the poller and its heartbeat

                Assert.That(WaitUntil(() => _costPlus.ResultOf(loginId).DateRated != null, TimeSpan.FromSeconds(20)), Is.True, "the poller did not answer the request");
                Assert.That(store.Attempts, Is.GreaterThan(0), "the heartbeat was tried, and failed");
            }

            Assert.That(_costPlus.ResultOf(loginId).Slots, Is.Not.Empty);
        }

        [Test]
        public void ARunningPollerAppearsInTheHeartbeatTableAsRunning()
        {
            SandboxGuard.Require();
            _cleanUpHeartbeat = true;
            var store = new SqlPollerHeartbeatStore();
            string me = Environment.MachineName + ":" + Environment.ProcessId;
            DeleteHeartbeat(me);   // other host tests in this process leave their own instance's row (same id); start from none

            using (WebApplicationFactory<Program> app = App())
            {
                app.CreateClient();

                Assert.That(WaitUntil(() => store.ReadRecent().Any(b => b.InstanceId == me), TimeSpan.FromSeconds(15)), Is.True, "no heartbeat arrived");
                CostPlusMonitorStatus status = CostPlusMonitorStatus.Judge(store.ReadRecent().Where(b => b.InstanceId == me), null);
                Assert.That(status.Instances.Single().Running, Is.True);
                Assert.That(status.Instances.Single().Mode, Is.EqualTo("Live"));
            }
        }
    }
}
