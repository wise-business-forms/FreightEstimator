using AuthenticationServer.Models;
using FreightEstimator.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The ASP.NET Core app (port stage 2, #29) hosted in memory: it starts, routes to the Plant page, serves its static
    /// assets, renders the views, and reads its data connection strings from configuration. This is the same pipeline IIS
    /// runs (in-process, through the ASP.NET Core Module); what it cannot show is IIS itself. SANDBOX and the carrier fake
    /// are the same as for the characterization tests.
    /// </summary>
    public class PlantPageHostTests
    {
        private WebApplicationFactory<Program> _factory;

        [SetUp]
        public void SetUp()
        {
            SandboxGuard.Require();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            Configuration.ConnectionStringSource = null;   // the app sets it at startup; a later test must not see a disposed host's
        }

        [Test]
        public async Task ThePlantPageRenders_WithItsFormAndItsAssets()
        {
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage response = await client.GetAsync("/Plant/Index?loc=ALP");
            string html = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), html);
            Assert.That(html, Does.Contain("Shipping From:").And.Contain("(ALP)"));
            // The form's posted names are what the Quote and the Plant page's script depend on.
            Assert.That(html, Does.Contain("name=\"number_of_packages\"").And.Contain("name=\"package_weight\"").And.Contain("name=\"State_selection\"").And.Contain("name=\"freight_class_selected\""));
            Assert.That(html, Does.Contain("action=\"/Plant/SubmitShipment\""));
        }

        [TestCase("/Content/bootstrap.css")]
        [TestCase("/Content/Site.css")]
        [TestCase("/Content/img/wiseLogo.jpg")]
        [TestCase("/Scripts/jquery-3.7.1.js")]
        [TestCase("/Scripts/bootstrap.js")]
        public async Task StaticAssetsAreServed(string path)
        {
            HttpResponseMessage response = await _factory.CreateClient().GetAsync(path);

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), path);
        }

        [Test]
        public async Task AnUnknownPlantIsTheErrorPage()
        {
            HttpResponseMessage response = await _factory.CreateClient().GetAsync("/Plant/Index?loc=ZZZ");

            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("An error occurred while processing your request."));
        }

        [Test]
        public async Task AQuoteIsShownOnTheConfirmationPage_ThroughTheRealPipeline()
        {
            QuoteCase c = QuoteCase.All("parcel").First(x => x.Packages == 1);
            var fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/", System.Net.HttpStatusCode.OK, c.ResponseText);

            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            using (fake.Install())
            {
                string query = "PlantId=" + c.Plant + "&Address=1%20Main%20St&City=Anytown&State_selection=" + c.ShipToState + "&Zip=" + c.ShipToZip
                    + "&Country_selection=US&number_of_packages=" + c.Packages + "&package_weight=" + c.PackageWeight.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&last_package_weight=0&AcctNum=0&multiple_location_rate_selection=No&include_ground_rate_selection=No&include_ltl_rate_selection=No";

                HttpResponseMessage response = await _factory.CreateClient().GetAsync("/Plant/ShipmentConfirmation?" + query);
                string html = await response.Content.ReadAsStringAsync();

                Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), html);
                // The page shows the same figure the characterization tests pin for this recorded quote (UPSGround, 1 package, non-CWT).
                decimal pinned = (decimal)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes", "expected", c.CaseId + ".json")))["UPSGround"]["calculatedRate"];
                Assert.That(html, Does.Contain(pinned.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("en-US"))).Or.Contain(Math.Round(pinned, 2).ToString("0.00")));
                Assert.That(fake.Requests, Is.Not.Empty, "the quote went through the carrier fake, not the network");
            }
        }

        [Test]
        public void ADevelopmentRunRefusesToStartOnAServerThatIsNotSandbox()
        {
            using (WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string> { ["ConnectionStrings:UpsRate"] = "Server=prod-db-01;Initial Catalog=UpsRate;" }))))
            {
                var ex = Assert.Catch<InvalidOperationException>(() => factory.CreateClient());

                Assert.That(ex.Message, Does.Contain("not recognisably SANDBOX"));
            }
        }

        [Test]
        public void ADevelopmentRunRefusesToStartOnTheUpsRateDatabaseEvenOnASandboxServer()
        {
            using (WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string> { ["ConnectionStrings:UpsRate"] = "Server=sandbox-configured-by-appsettings;Initial Catalog=UpsRate;" }))))
            {
                var ex = Assert.Catch<InvalidOperationException>(() => factory.CreateClient());

                Assert.That(ex.Message, Does.Contain("not FreightEstimator"));
            }
        }

        [Test]
        public void TheDataConnectionStringsComeFromConfiguration_ForTheEnvironment()
        {
            using (WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string> { ["ConnectionStrings:UpsRate"] = "Server=sandbox-configured-by-appsettings;Initial Catalog=FreightEstimator;" }))))
            {
                factory.CreateClient();   // starts the host, which hands its configuration to the estimation core

                Assert.That(Configuration.UpsRateSqlConnection, Is.EqualTo("Server=sandbox-configured-by-appsettings;Initial Catalog=FreightEstimator;"));
            }
        }
    }
}
