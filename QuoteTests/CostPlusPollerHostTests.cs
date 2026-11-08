using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The CostPlus poller (#31, ADR 0005) as part of the running app: a background service that answers requests in the
    /// CostPlus request table. The app is hosted in memory with the carrier fake installed, so nothing reaches UPS.
    /// </summary>
    public class CostPlusPollerHostTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown()
        {
            _costPlus.Dispose();
            Configuration.ConnectionStringSource = null;   // the app sets it at startup; a later test must not see a disposed host's
        }

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        private static WebApplicationFactory<Program> App(bool pollerEnabled) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string> { ["CostPlus:Poller:Enabled"] = pollerEnabled ? "true" : "false", ["CostPlus:Poller:Mode"] = "Live" }));
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
        public void ThePollerRunsAsABackgroundServiceInsideTheApp()
        {
            QuoteCase c = Case;
            string loginId = _costPlus.Add(SandboxCostPlus.For(c));
            FakeCarrierHttp fake = QuoteFlow.FakeFor(c);

            using (new SandboxPlantCharges("ALP", SandboxPlantCharges.KnownSchedule()))
            using (ServiceCatalog.Use(CostPlusCycle.ParcelServices))
            using (fake.Install())
            using (WebApplicationFactory<Program> app = App(pollerEnabled: true))
            {
                app.CreateClient();   // starts the host, and with it the poller

                Assert.That(WaitUntil(() => _costPlus.ResultOf(loginId).DateRated != null, TimeSpan.FromSeconds(20)), Is.True, "the poller did not answer the request");
            }

            Assert.That(_costPlus.ResultOf(loginId).Slots, Is.Not.Empty);
        }

        [Test]
        public void ThePollerDoesNothingUnlessItIsSwitchedOn()
        {
            QuoteCase c = Case;
            string loginId = _costPlus.Add(SandboxCostPlus.For(c));
            var fake = new FakeCarrierHttp();   // nothing set up: any carrier call throws

            using (fake.Install())
            using (WebApplicationFactory<Program> app = App(pollerEnabled: false))
            {
                app.CreateClient();
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }

            Assert.That(_costPlus.ClaimOf(loginId), Is.Null);
            Assert.That(_costPlus.ResultOf(loginId).DateRated, Is.Null);
            Assert.That(fake.Requests, Is.Empty);
        }
    }
}
