using System.Text.Json;
using AuthenticationServer.CostPlus;
using AuthenticationServer.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The Poller Monitor page and the status it re-fetches about every 2 s (#49, #50): open only to the allowed users, rendering
    /// whatever the instances and the queue report. The database sits behind <see cref="IPollerMonitorSource"/>, which these tests
    /// replace; the SQL itself is covered by the heartbeat store and queue snapshot tests.
    /// </summary>
    public class PollerMonitorStatusTests
    {
        private const string Allowed = "WBF\\pmenefee";
        private const string StatusUrl = "/Admin/PollerMonitorStatus";

        [TearDown]
        public void TearDown() => Configuration.ConnectionStringSource = null;   // the app sets it at startup; a later test must not see a disposed host's

        private class FakeSource : IPollerMonitorSource
        {
            public MonitorReading Reading = new MonitorReading { Beats = new List<PollerInstanceBeat>(), Queue = new QueueSnapshot() };
            public int Reads;
            public MonitorReading Read() { Reads++; return Reading; }
        }

        private static WebApplicationFactory<Program> App(FakeSource source) =>
            AdminHostTests.App("Production", services => services.AddSingleton<IPollerMonitorSource>(source), new[] { Allowed });

        private static PollerInstanceBeat Beat(string id, string mode = "Live", double heartbeatAge = 3) =>
            new PollerInstanceBeat
            {
                InstanceId = id, Machine = id.Split(':')[0], Mode = mode, HeartbeatAge = TimeSpan.FromSeconds(heartbeatAge),
                SinceLastSuccessfulPoll = TimeSpan.FromSeconds(1), StartedAgo = TimeSpan.FromMinutes(10),
                IdlePolls = 120, RequestsHandled = 9, DroppedEvents = 0
            };

        private static async Task<JsonElement> StatusAsync(WebApplicationFactory<Program> app, string user = Allowed)
        {
            HttpResponseMessage response = await AdminHostTests.ClientFor(app, user).GetAsync(StatusUrl);
            string body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), body);
            return JsonDocument.Parse(body).RootElement;
        }

        [Test]
        public async Task TheStatusIsOpenOnlyToAllowedUsers()
        {
            using var app = App(new FakeSource());

            Assert.That((await AdminHostTests.ClientFor(app, Allowed).GetAsync(StatusUrl)).StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            Assert.That((await AdminHostTests.ClientFor(app, "WBF\\someoneelse").GetAsync(StatusUrl)).StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
            Assert.That((await AdminHostTests.ClientFor(app).GetAsync(StatusUrl)).StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task WithNoInstancesTheStatusSaysNoPollerIsRunning()
        {
            using var app = App(new FakeSource());

            JsonElement status = await StatusAsync(app);

            Assert.That(status.GetProperty("anyRunning").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("healthy").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("instances").GetArrayLength(), Is.EqualTo(0));
            Assert.That(status.GetProperty("problems")[0].GetString(), Does.Contain("No poller is running"));
        }

        [Test]
        public async Task TwoOverlappingInstancesAreBothInTheStatus()
        {
            var source = new FakeSource();
            source.Reading.Beats = new List<PollerInstanceBeat> { Beat("ALPWEB:1", "Live", heartbeatAge: 4), Beat("ALPWEB:2", "Live", heartbeatAge: 2) };
            using var app = App(source);

            JsonElement status = await StatusAsync(app);

            Assert.That(status.GetProperty("anyRunning").GetBoolean(), Is.True);
            Assert.That(status.GetProperty("healthy").GetBoolean(), Is.True);
            JsonElement instances = status.GetProperty("instances");
            Assert.That(instances.GetArrayLength(), Is.EqualTo(2));
            JsonElement first = instances[0];
            Assert.That(first.GetProperty("instanceId").GetString(), Is.EqualTo("ALPWEB:2"), "the most recent heartbeat first");
            Assert.That((first.GetProperty("mode").GetString(), first.GetProperty("running").GetBoolean(), first.GetProperty("requestsHandled").GetInt64()), Is.EqualTo(("Live", true, 9L)));
        }

        [Test]
        public async Task AStoppedInstanceIsShownAsStopped()
        {
            var source = new FakeSource();
            source.Reading.Beats = new List<PollerInstanceBeat> { Beat("ALPWEB:1", heartbeatAge: 300) };
            using var app = App(source);

            JsonElement status = await StatusAsync(app);

            Assert.That(status.GetProperty("instances")[0].GetProperty("running").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("anyRunning").GetBoolean(), Is.False);
        }

        [Test]
        public async Task TheQueueIsReportedWithoutStreetAddresses()
        {
            var source = new FakeSource();
            source.Reading.Beats = new List<PollerInstanceBeat> { Beat("ALPWEB:1") };
            source.Reading.Queue = new QueueSnapshot
            {
                Waiting = 12,
                Oldest = new List<QueueRequest> { new QueueRequest { LoginId = "jdoe", QtyNumber = 1, FromState = "GA", ToZip = "30004", Ups = true, AgeSeconds = 4.5 } },
                InFlight = new List<QueueRequest> { new QueueRequest { LoginId = "asmith", QtyNumber = 2, ClaimedBy = "ALPWEB:1", AgeSeconds = 1.2 } },
                LatestProbe = new ProbeStatus { SentAgeSeconds = 130, Answered = true }
            };
            using var app = App(source);

            JsonElement status = await StatusAsync(app);

            JsonElement queue = status.GetProperty("queue");
            Assert.That(queue.GetProperty("waiting").GetInt32(), Is.EqualTo(12));
            JsonElement oldest = queue.GetProperty("oldest")[0];
            Assert.That((oldest.GetProperty("loginId").GetString(), oldest.GetProperty("fromState").GetString(), oldest.GetProperty("toZip").GetString()), Is.EqualTo(("jdoe", "GA", "30004")));
            Assert.That(queue.GetProperty("inFlight")[0].GetProperty("claimedBy").GetString(), Is.EqualTo("ALPWEB:1"));
            Assert.That(oldest.EnumerateObject().Select(p => p.Name), Has.None.Contains("ddress"));
            Assert.That(status.GetProperty("probe").GetProperty("latest").GetProperty("answered").GetBoolean(), Is.True);
        }

        [Test]
        public async Task ALiveRequestWaitingTooLongMakesThePollerUnhealthy()
        {
            var source = new FakeSource();
            source.Reading.Beats = new List<PollerInstanceBeat> { Beat("ALPWEB:1", "Live") };
            source.Reading.Queue = new QueueSnapshot { Waiting = 1, Oldest = new List<QueueRequest> { new QueueRequest { LoginId = "jdoe", AgeSeconds = 45 } } };
            using var app = App(source);

            JsonElement status = await StatusAsync(app);

            Assert.That(status.GetProperty("healthy").GetBoolean(), Is.False);
            Assert.That(status.GetProperty("problems")[0].GetString(), Does.Contain("oldest unclaimed request"));
        }

        [Test]
        public async Task APartThatCannotBeReadIsReportedAndTheRestStillShows()
        {
            var source = new FakeSource();
            source.Reading = new MonitorReading { Beats = new List<PollerInstanceBeat> { Beat("ALPWEB:1") }, QueueError = "The request table could not be read." };
            using var app = App(source);

            JsonElement status = await StatusAsync(app);

            Assert.That(status.GetProperty("instances").GetArrayLength(), Is.EqualTo(1));
            Assert.That(status.GetProperty("queue").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(status.GetProperty("errors")[0].GetString(), Does.Contain("request table"));
        }

        [Test]
        public async Task WhenTheInstancesCannotBeReadTheStatusDoesNotClaimNoPollerIsRunning()
        {
            var source = new FakeSource();
            source.Reading = new MonitorReading { BeatsError = "The heartbeat table could not be read.", Queue = new QueueSnapshot() };
            using var app = App(source);

            JsonElement status = await StatusAsync(app);

            Assert.That(status.GetProperty("errors")[0].GetString(), Does.Contain("heartbeat table"));
            Assert.That(status.GetProperty("problems").EnumerateArray().Select(p => p.GetString()), Has.None.Contain("No poller is running"));
        }

        [Test]
        public async Task ThePageHasItsPanelsAndAPauseToggle_AndRefetchesTheStatus()
        {
            using var app = App(new FakeSource());

            HttpResponseMessage response = await AdminHostTests.ClientFor(app, Allowed).GetAsync("/Admin/PollerMonitor");
            string html = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            Assert.That(html, Does.Contain("Instances").And.Contain("Waiting queue").And.Contain("id=\"pause\""));
            Assert.That(html, Does.Contain(StatusUrl));
        }
    }
}
