using System.Net;
using System.Net.Http;
using System.Text;
#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
#else
using System.Web.Http;
#endif
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The Quote API skeleton (#18) at its HTTP seam: real requests through the Web API pipeline, a SANDBOX
    /// database, and a carrier fake that fails the test if anything reaches UPS or the Logistics Partner (no path
    /// is available yet, so no carrier may be called). Asserts on responses and on what was logged, nothing else.
    /// </summary>
    public class QuoteApiTests
    {
        private SandboxConsumer _consumer;
        private FakeCarrierHttp _carriers;
        private IDisposable _carrierScope;
        private HttpClient _client;
#if NET8_0_OR_GREATER
        private WebApplicationFactory<Program> _factory;
#endif

        [SetUp]
        public void SetUp()
        {
            _consumer = new SandboxConsumer();
            _carriers = new FakeCarrierHttp();   // no responses set up: any carrier call throws
            _carrierScope = _carriers.Install();

#if NET8_0_OR_GREATER
            // The ASP.NET Core app (port stage 3, #30), hosted in memory: the same requests, the same assertions.
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
            _client = _factory.CreateClient();
#else
            var config = new HttpConfiguration();
            AuthenticationServer.WebApiConfig.Register(config);
            config.EnsureInitialized();
            _client = new HttpClient(new HttpServer(config)) { BaseAddress = new Uri("http://localhost/") };
#endif
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
#if NET8_0_OR_GREATER
            _factory.Dispose();
            AuthenticationServer.Models.Configuration.ConnectionStringSource = null;   // the app sets it at startup; a later test must not see a disposed host's
#endif
            _carrierScope.Dispose();
            _consumer.Dispose();
        }

        // ---- authentication -------------------------------------------------------------------------------------

        [Test]
        public void ARequestWithoutAnApiKeyIs401()
        {
            Assert.That(Get("api/v1/plants", key: null).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Post(ValidRequest(), key: null).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(_consumer.Quotes(), Is.Empty);
        }

        [Test]
        public void ARequestWithAnUnknownApiKeyIs401()
        {
            Assert.That(Get("api/v1/plants", key: "not-a-real-key").StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(Post(ValidRequest(), key: "not-a-real-key").StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(_consumer.Quotes(), Is.Empty);
        }

        [Test]
        public void ARevokedConsumersKeyIs401()
        {
            using (var revoked = new SandboxConsumer(isActive: false))
            {
                Assert.That(Post(ValidRequest(), key: revoked.ApiKey).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(revoked.Quotes(), Is.Empty);
            }
        }

        // ---- plants ---------------------------------------------------------------------------------------------

        [Test]
        public void PlantsListsTheValidPlantCodes()
        {
            HttpResponseMessage response = Get("api/v1/plants", _consumer.ApiKey);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            string[] codes = JArray.Parse(Body(response)).Select(t => (string)t).ToArray();
            Assert.That(codes, Does.Contain("ALP").And.Contain("BUT"));
            // SANDBOX's CIN has no UPS shipping number, so it is not a plant a Quote can be requested for.
            Assert.That(codes, Does.Not.Contain("CIN"));
        }

        // ---- validation: 400, naming the field ------------------------------------------------------------------

        [TestCase("referenceNumber", "remove")]
        [TestCase("referenceNumber", "blank")]
        [TestCase("packages", "remove")]
        [TestCase("packages", "empty")]
        [TestCase("packages[0].weight", "zero")]
        [TestCase("packages[1].weight", "negative")]
        [TestCase("packages[0].weight", "remove")]
        [TestCase("plantCode", "remove")]
        [TestCase("plantCode", "unknown")]
        [TestCase("services[0]", "unknown")]
        [TestCase("destination.postalCode", "remove")]
        [TestCase("destination.state", "remove")]
        public void ABadRequestIs400NamingTheField_AndIsNotLogged(string field, string fault)
        {
            JObject body = ValidRequest();
            Break(body, field, fault);

            HttpResponseMessage response = Post(body, _consumer.ApiKey);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ErrorFields(response), Does.Contain(field));
            Assert.That(_consumer.Quotes(), Is.Empty, "a rejected request must not create a Quote");
        }

        [Test]
        public void EveryProblemIsReportedAtOnce()
        {
            JObject body = ValidRequest();
            body.Remove("referenceNumber");
            body["packages"] = new JArray();

            HttpResponseMessage response = Post(body, _consumer.ApiKey);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ErrorFields(response), Is.EquivalentTo(new[] { "referenceNumber", "packages" }));
        }

        [Test]
        public void ARequestThatIsNotJsonIs400()
        {
            HttpResponseMessage response = _client.SendAsync(Authenticated(
                new HttpRequestMessage(HttpMethod.Post, "api/v1/quotes") { Content = new StringContent("{ not json", Encoding.UTF8, "application/json") },
                _consumer.ApiKey)).Result;

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(_consumer.Quotes(), Is.Empty);
        }

        // ---- a valid request ------------------------------------------------------------------------------------

        [Test]
        public void AValidRequestReturnsAQuoteIdAndEveryRequestedServiceNotAvailable()
        {
            JObject body = ValidRequest();
            body["services"] = new JArray("UPSGround", "LTL");

            HttpResponseMessage response = Post(body, _consumer.ApiKey);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            JObject quote = JObject.Parse(Body(response));
            Assert.That(Guid.TryParse((string)quote["quoteId"], out Guid id) && id != Guid.Empty, "quoteId");
            Assert.That((string)quote["referenceNumber"], Is.EqualTo("JOB-1001"));
            Assert.That(quote["services"].Select(s => (string)s["service"]), Is.EquivalentTo(new[] { "UPSGround", "LTL" }));
            Assert.That(quote["services"].Select(s => (string)s["availability"]), Is.All.EqualTo("notAvailable"));
            Assert.That(_carriers.Requests, Is.Empty, "no carrier may be called while no path is available");
        }

        [Test]
        public void WithNoServicesNamedEveryServiceInTheCatalogIsReportedNotAvailable()
        {
            JObject body = ValidRequest();
            body.Remove("services");

            JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

            string[] services = quote["services"].Select(s => (string)s["service"]).ToArray();
            Assert.That(services, Does.Contain("UPSGround").And.Contain("UPSGroundFreight").And.Contain("LTL"));
            Assert.That(quote["services"].Select(s => (string)s["availability"]), Is.All.EqualTo("notAvailable"));
        }

        [Test]
        public void TheQuoteIsLoggedWithTheConsumerAndTheReferenceNumber()
        {
            HttpResponseMessage response = Post(ValidRequest(), _consumer.ApiKey);

            Guid quoteId = Guid.Parse((string)JObject.Parse(Body(response))["quoteId"]);
            var logged = _consumer.Quotes().Single();
            Assert.That(logged.QuoteId, Is.EqualTo(quoteId));
            Assert.That(logged.ConsumerId, Is.EqualTo(_consumer.ConsumerId));
            Assert.That(logged.ReferenceNumber, Is.EqualTo("JOB-1001"));
            Assert.That(logged.PlantCode, Is.EqualTo("ALP"));
            Assert.That(JObject.Parse(logged.RequestJson)["packages"].Count(), Is.EqualTo(2), "the request is kept as it was sent");
        }

        [Test]
        public void TwoIdenticalCallsCreateTwoQuotesWithTwoQuoteIds()
        {
            string first = (string)JObject.Parse(Body(Post(ValidRequest(), _consumer.ApiKey)))["quoteId"];
            string second = (string)JObject.Parse(Body(Post(ValidRequest(), _consumer.ApiKey)))["quoteId"];

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(_consumer.Quotes().Select(q => q.QuoteId.ToString()), Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public void EachQuoteIsAttributedToTheConsumerThatAskedForIt()
        {
            using (var other = new SandboxConsumer())
            {
                Post(ValidRequest(), _consumer.ApiKey);
                Post(ValidRequest(), other.ApiKey);
                Post(ValidRequest(), other.ApiKey);

                Assert.That(_consumer.Quotes(), Has.Count.EqualTo(1));
                Assert.That(other.Quotes(), Has.Count.EqualTo(2));
            }
        }

        // ---- Ground Freight (GRID2) available through the API (#19) --------------------------------------------

        public static IEnumerable<TestCaseData> GroundFreightCases() =>
            QuoteCase.All("groundfreight").Select(c => new TestCaseData(c).SetName("groundfreight " + c.CaseId + " " + c.Plant + " " + c.Packages + " pkgs"));

        [TestCaseSource(nameof(GroundFreightCases))]
        public void GroundFreightIsAvailableAndReturnsTheRoundedCalculatedRateWithItsBreakdown(QuoteCase c)
        {
            UpsReplies(c);
            JObject body = GroundFreightRequest(c);

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                JToken service = quote["services"].Single();
                Assert.That((string)service["service"], Is.EqualTo("UPSGroundFreight"));
                Assert.That((string)service["availability"], Is.EqualTo("available"));

                // The Plant page shows the same Calculated Rate for this shipment (pinned in #15), unrounded;
                // the API returns it rounded half-up to the cent (#13).
                decimal onThePlantPage = (decimal)JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes", "expected", c.CaseId + ".json")))
                    .Properties().Single().Value["calculatedRate"];
                Assert.That((decimal)service["calculatedRate"], Is.EqualTo(Math.Round(onThePlantPage, 2, MidpointRounding.AwayFromZero)));

                // The breakdown: the Carrier Rate before our charges, and the Plant Charges applied, unrounded.
                // SANDBOX's GF row for the plant is 5% markup, 2.00 per package, 5.00 per shipment (KnownSchedule).
                Assert.That((decimal)service["carrierRate"], Is.GreaterThan(0m));
                Assert.That((decimal)service["plantCharges"]["markupPercent"], Is.EqualTo(5m));
                Assert.That((decimal)service["plantCharges"]["perPackageCharge"], Is.EqualTo(2m));
                Assert.That((decimal)service["plantCharges"]["perShipmentCharge"], Is.EqualTo(5m));

                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]), Is.EqualTo(new[] { "UPS:ok" }));
                Assert.That((string)quote["addressClassification"], Is.EqualTo("Commercial"));
                Assert.That(quote["warnings"], Is.Empty);
            }
        }

        [Test]
        public void TheCalculatedRateIsLoggedPerServicePerQuoteAsForPlantPageQuotes()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            UpsReplies(c);

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(GroundFreightRequest(c), _consumer.ApiKey)));

                var logged = _consumer.Calculations(Guid.Parse((string)quote["quoteId"])).Single();
                Assert.That(logged.Carrier, Is.EqualTo("UPS"));
                Assert.That(logged.ServiceName, Is.EqualTo("UPSGroundFreight"));
                Assert.That(logged.PlantCode, Is.EqualTo(c.Plant));
                Assert.That(logged.UserName, Is.EqualTo(_consumer.Name), "the Consumer replaces the Plant page's 'TBD' requester");
                Assert.That(logged.CalculatedRate, Is.EqualTo((decimal)quote["services"][0]["calculatedRate"]));
                Assert.That(logged.CarrierRate, Is.EqualTo((decimal)quote["services"][0]["carrierRate"]));
                Assert.That(logged.MarkupPercent, Is.EqualTo(5m));
                Assert.That(logged.PerPackageCharge, Is.EqualTo(2m));
                Assert.That(logged.PerShipmentCharge, Is.EqualTo(5m));
            }
        }

        [Test]
        public void GroundFreightIsNotAvailableUntilItIsSwitchedOn_AndUpsIsNotCalled()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            UpsReplies(c);

            JObject quote = JObject.Parse(Body(Post(GroundFreightRequest(c), _consumer.ApiKey)));

            Assert.That((string)quote["services"][0]["availability"], Is.EqualTo("notAvailable"));
            Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
            Assert.That(_carriers.Requests, Is.Empty);
        }

        [Test]
        public void UpsIsNotCalledWhenNoUpsServiceWasAskedFor()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            UpsReplies(c);
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("LTL");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                Assert.That(quote["services"].Select(s => (string)s["service"]), Is.EqualTo(new[] { "LTL" }));
                Assert.That(quote["carriers"], Is.Empty);
                Assert.That(_carriers.Requests, Is.Empty);
            }
        }

        [Test]
        public void AUpsTimeoutIsA200WithTheCarrierUnavailable_AndTheQuoteStillLogged()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json").TimeOut("rating/v2403/Rate");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                HttpResponseMessage response = Post(GroundFreightRequest(c), _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]), Is.EqualTo(new[] { "UPS:unavailable" }));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null, "no number when the carrier did not answer");
                Assert.That(_consumer.Quotes().Select(q => q.QuoteId.ToString()), Is.EqualTo(new[] { (string)quote["quoteId"] }));
                Assert.That(_consumer.Calculations(Guid.Parse((string)quote["quoteId"])), Is.Empty);
            }
        }

        [Test]
        public void AUpsTimeoutOnTheAddressCheckIsAlsoUnavailable()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").TimeOut("addressvalidation");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            {
                JObject quote = JObject.Parse(Body(Post(GroundFreightRequest(c), _consumer.ApiKey)));

                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("unavailable"));
            }
        }

        [Test]
        public void AUpsErrorReplyIsA200WithTheCarrierInError()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Rate", HttpStatusCode.BadRequest,
                    "{\"response\":{\"errors\":[{\"code\":\"111210\",\"message\":\"The requested service is unavailable between the selected locations.\"}]}}");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                HttpResponseMessage response = Post(GroundFreightRequest(c), _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("error"));
                Assert.That((string)quote["carriers"][0]["message"], Does.Contain("unavailable between the selected locations"));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
                Assert.That(_consumer.Quotes(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void AUpsReplyThatCannotBeUsedIsA200WithTheCarrierInError_NotA500()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Rate", HttpStatusCode.OK, "{}");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            {
                HttpResponseMessage response = Post(GroundFreightRequest(c), _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("error"));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
                Assert.That(_consumer.Quotes(), Has.Count.EqualTo(1), "the Quote is still logged");
            }
        }

        [Test]
        public void ARateOfZeroIsNotQuotedAsIfItWereAPrice()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Rate", HttpStatusCode.OK,
                    "{\"RateResponse\":{\"Response\":{\"ResponseStatus\":{\"Code\":\"1\"},\"Alert\":[{\"Code\":\"1\",\"Description\":\"x\"}]},\"RatedShipment\":{\"Service\":{\"Code\":\"03\"},\"TotalCharges\":{\"CurrencyCode\":\"USD\",\"MonetaryValue\":\"0.00\"}}}}");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(GroundFreightRequest(c), _consumer.ApiKey)));

                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("error"));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null, "the plant's flat charges must not turn a missing rate into a price");
                Assert.That(_consumer.Calculations(Guid.Parse((string)quote["quoteId"])), Is.Empty);
            }
        }

        [Test]
        public void TheUnavailableMessageDoesNotCarryExceptionTextOrHostNames()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json").TimeOut("rating/v2403/Rate");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            {
                JObject quote = JObject.Parse(Body(Post(GroundFreightRequest(c), _consumer.ApiKey)));

                Assert.That((string)quote["carriers"][0]["message"], Is.EqualTo("UPS did not answer in time."));
            }
        }

        [Test]
        public void AnUnknownAddressClassificationAddsAWarning()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-unknown.json")
                .RespondWith("rating/v2403/Rate", HttpStatusCode.OK, c.ResponseText);

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(GroundFreightRequest(c), _consumer.ApiKey)));

                Assert.That((string)quote["addressClassification"], Is.EqualTo("Unknown"));
                Assert.That(quote["warnings"].Select(w => (string)w["code"]), Is.EqualTo(new[] { "addressClassificationUnknown" }));
                Assert.That((string)quote["warnings"][0]["message"], Does.Contain("residential"));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Not.Null, "a warning does not withhold the rate");
            }
        }

        [Test]
        public void AUpsFailureLeavesTheOtherRequestedServicesAsTheyWere()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json").TimeOut("rating/v2403/Rate");
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGroundFreight", "LTL");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            {
                HttpResponseMessage response = Post(body, _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That(quote["services"].Select(s => (string)s["service"] + ":" + (string)s["availability"]),
                    Is.EqualTo(new[] { "UPSGroundFreight:available", "LTL:notAvailable" }));
            }
        }

        [Test]
        public void EachPackageWeightInTheRequestReachesUps()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            UpsReplies(c);
            JObject body = GroundFreightRequest(c);
            body["packages"] = new JArray(new JObject { ["weight"] = 31 }, new JObject { ["weight"] = 17 }, new JObject { ["weight"] = 9 });

            using (ServiceCatalog.Use("UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                Post(body, _consumer.ApiKey);

                // Three different weights cannot be told to UPS as "n packages and a last one"; each goes through as given.
                string rateRequest = _carriers.Requests.Single(r => r.Url.Contains("rating/v2403/Rate")).Body;
                var weights = JObject.Parse(rateRequest)["RateRequest"]["Shipment"]["Package"].Select(p => (string)p["PackageWeight"]["Weight"]).ToArray();
                Assert.That(weights, Is.EqualTo(new[] { "31", "17", "9" }));
                Assert.That((string)JObject.Parse(rateRequest)["RateRequest"]["Shipment"]["NumOfPieces"], Is.EqualTo("3"));
            }
        }

        // ---- UPS parcel (GRID1 single plant) available through the API (#20) -----------------------------------

        public static IEnumerable<TestCaseData> ParcelCases() =>
            QuoteCase.All("parcel").Select(c => new TestCaseData(c).SetName("parcel " + c.CaseId + " " + c.Plant + " " + c.Packages + " pkgs"));

        private static readonly string[] ParcelServices =
            { "UPSGround", "UPS3DaySelect", "UPS2ndDayAir", "SecondDayAirAM", "NextDayAirSaver", "UPSNextDayAir", "NextDayAirEarlyAM" };

        /// <summary>
        /// Every recorded parcel case, CWT and non-CWT services alike: the Calculated Rate is the Carrier Rate (negotiated
        /// when CWT, published otherwise) plus markup, per-package and per-shipment charges from the UPS row, or the UPSCWT
        /// row when CWT, as set by KnownSchedule. Markup applies to non-CWT too (#14), unlike the Plant page today.
        /// </summary>
        [TestCaseSource(nameof(ParcelCases))]
        public void ParcelServicesAreAvailableAndReturnTheRoundedCalculatedRateWithTheirBreakdown(QuoteCase c)
        {
            UpsParcelReplies(c);
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray(ParcelServices);

            using (ServiceCatalog.Use(ParcelServices))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                JObject expectedOnPage = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes", "expected", c.CaseId + ".json")));
                JToken recorded = JObject.Parse(c.ResponseText)["RateResponse"]["RatedShipment"];
                int quoted = 0;
                foreach (JToken service in quote["services"])
                {
                    string name = (string)service["service"];
                    Assert.That((string)service["availability"], Is.EqualTo("available"), name);
                    JToken onPage = expectedOnPage[name];
                    if (onPage == null)
                    {
                        Assert.That(service["calculatedRate"], Is.Null, name + " was not in UPS's answer");
                        continue;
                    }
                    quoted++;

                    bool cwt = (string)onPage["cwt"] == "Yes";
                    SandboxPlantCharges.Row row = SandboxPlantCharges.KnownSchedule().Single(r => r.CarrierId == (cwt ? "UPSCWT" : "UPS"));
                    decimal markup = MarkupFor(row, name);
                    JToken rated = recorded.Single(r => ServiceNameOf((string)r["Service"]["Code"]) == name);
                    decimal carrierRate = cwt
                        ? (decimal)rated["NegotiatedRateCharges"]["TotalCharge"]["MonetaryValue"]
                        : (decimal)rated["TotalCharges"]["MonetaryValue"];
                    decimal expected = carrierRate + carrierRate * markup / 100m + row.PerPackage * c.Packages + row.PerShipment;

                    Assert.That((decimal)service["calculatedRate"], Is.EqualTo(Math.Round(expected, 2, MidpointRounding.AwayFromZero)), name);
                    Assert.That((decimal)service["carrierRate"], Is.EqualTo(carrierRate), name);
                    Assert.That((decimal)service["plantCharges"]["markupPercent"], Is.EqualTo(markup), name);
                    Assert.That((decimal)service["plantCharges"]["perPackageCharge"], Is.EqualTo(row.PerPackage), name);
                    Assert.That((decimal)service["plantCharges"]["perShipmentCharge"], Is.EqualTo(row.PerShipment), name);
                }

                Assert.That(quoted, Is.GreaterThan(0));
                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]), Is.EqualTo(new[] { "UPS:ok" }));
            }
        }

        [Test]
        public void TheRecordedParcelCasesIncludeBothCwtAndNonCwtServices()
        {
            // Guards the test above: it must exercise services that were CWT and services that were not.
            string pinned = string.Concat(QuoteCase.All("parcel").Select(c =>
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes", "expected", c.CaseId + ".json"))));
            Assert.That(pinned, Does.Contain("\"cwt\": \"Yes\"").And.Contain("\"cwt\": \"No\""));
        }

        [Test]
        public void ParcelRatesAreLoggedPerServicePerQuote()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsParcelReplies(c);
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGround");

            using (ServiceCatalog.Use("UPSGround"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                var logged = _consumer.Calculations(Guid.Parse((string)quote["quoteId"])).Single();
                Assert.That(logged.ServiceName, Is.EqualTo("UPSGround"));
                Assert.That(logged.UserName, Is.EqualTo(_consumer.Name));
                Assert.That(logged.CalculatedRate, Is.EqualTo((decimal)quote["services"][0]["calculatedRate"]));
                Assert.That(logged.CarrierRate, Is.EqualTo((decimal)quote["services"][0]["carrierRate"]));
            }
        }

        [Test]
        public void ParcelServicesAreNotAvailableUntilSwitchedOn_AndUpsIsNotCalled()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsParcelReplies(c);
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGround", "UPS2ndDayAir");

            using (ServiceCatalog.Use("UPSGroundFreight"))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                Assert.That(quote["services"].Select(s => (string)s["availability"]), Is.All.EqualTo("notAvailable"));
                Assert.That(quote["services"].Select(s => s["calculatedRate"]), Is.All.Null);
                Assert.That(_carriers.Requests, Is.Empty);
            }
        }

        [Test]
        public void OnlyTheParcelServicesSwitchedOnAreQuoted()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            UpsParcelReplies(c);
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGround", "UPS2ndDayAir");

            using (ServiceCatalog.Use("UPSGround"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                Assert.That(quote["services"].Select(s => (string)s["service"] + ":" + (string)s["availability"]),
                    Is.EqualTo(new[] { "UPSGround:available", "UPS2ndDayAir:notAvailable" }));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Not.Null);
                Assert.That(quote["services"][1]["calculatedRate"], Is.Null);
            }
        }

        [Test]
        public void AResidentialAddressIsRatedWithTheResidentialIndicator_SoTheSurchargeIsInTheCarrierRate()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-residential.json")
                .RespondWith("rating/v2403/Shop", HttpStatusCode.OK, c.ResponseText);
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGround");

            using (ServiceCatalog.Use("UPSGround"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                Assert.That((string)quote["addressClassification"], Is.EqualTo("Residential"));
                string rateRequest = _carriers.Requests.Single(r => r.Url.Contains("rating/v2403/Shop")).Body;
                Assert.That(JObject.Parse(rateRequest)["RateRequest"]["Shipment"]["ShipTo"]["Address"]["ResidentialAddressIndicator"], Is.Not.Null);
            }
        }

        [Test]
        public void AUpsTimeoutOnParcelIsA200WithTheCarrierUnavailable()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json").TimeOut("rating/v2403/Shop");
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGround");

            using (ServiceCatalog.Use("UPSGround"))
            {
                HttpResponseMessage response = Post(body, _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("unavailable"));
                Assert.That((string)quote["services"][0]["availability"], Is.EqualTo("available"));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
                Assert.That(_consumer.Quotes(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void ParcelAndGroundFreightTogetherAreOneUpsCarrierWithTheWorstStatus()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Shop", HttpStatusCode.OK, c.ResponseText).TimeOut("rating/v2403/Rate");
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("UPSGround", "UPSGroundFreight");

            using (ServiceCatalog.Use("UPSGround", "UPSGroundFreight"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]), Is.EqualTo(new[] { "UPS:unavailable" }));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Not.Null, "parcel was still quoted");
                Assert.That(quote["services"][1]["calculatedRate"], Is.Null);
            }
        }

        // ---- LTL (Logistics Partner) available through the API (#21) -------------------------------------------

        public static IEnumerable<TestCaseData> LtlCases() =>
            QuoteCase.All("ltl").Select(c => new TestCaseData(c).SetName("ltl " + c.CaseId + " " + c.Plant + " " + c.Packages + " pkgs"));

        /// <summary>
        /// Every recorded LTL case: the API's LTL is the Logistics Partner's cheapest option, priced by the unified module
        /// (the same markup, per-package and per-shipment charges the Plant page applies to each option today) and rounded.
        /// </summary>
        [TestCaseSource(nameof(LtlCases))]
        public void LtlIsAvailableAndReturnsTheCheapestOptionsRoundedCalculatedRateWithItsBreakdown(QuoteCase c)
        {
            LtlReplies(c);
            JObject body = LtlRequest(c);

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                JToken service = quote["services"].Single();
                Assert.That((string)service["service"], Is.EqualTo("LTL"));
                Assert.That((string)service["availability"], Is.EqualTo("available"));

                // What the Plant page shows for each option, pinned in #15.
                var options = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes", "expected", c.CaseId + ".json")))
                    .Properties()
                    .Select(p => new
                    {
                        OnThePage = decimal.Parse((string)p.Value["calculatedRate"], System.Globalization.NumberStyles.Currency, System.Globalization.CultureInfo.GetCultureInfo("en-US")),
                        CarrierRate = (decimal)p.Value["carrierRate"]
                    })
                    .OrderBy(o => o.OnThePage)
                    .ToList();

                Assert.That((decimal)service["calculatedRate"], Is.EqualTo(options[0].OnThePage));
                Assert.That((decimal)service["carrierRate"], Is.EqualTo(options[0].CarrierRate));
                // SANDBOX's TI row for the plant is 7.5% markup, 3.00 per package, 6.00 per shipment (KnownSchedule).
                Assert.That((decimal)service["plantCharges"]["markupPercent"], Is.EqualTo(7.5m));
                Assert.That((decimal)service["plantCharges"]["perPackageCharge"], Is.EqualTo(3m));
                Assert.That((decimal)service["plantCharges"]["perShipmentCharge"], Is.EqualTo(6m));

                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]), Is.EqualTo(new[] { "LogisticsPartner:ok" }));
            }
        }

        [Test]
        public void TheRequestsFreightClassIsWhatTheLogisticsPartnerIsAskedToRate()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LtlReplies(c);
            JObject body = LtlRequest(c);
            body["freightClass"] = 70;

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                Post(body, _consumer.ApiKey);

                string sent = _carriers.Requests.Single(r => r.Url.Contains(LtlHost)).Body;
                Assert.That(sent, Does.Contain("freightClass=\"70\""));
                Assert.That(sent, Does.Not.Contain("freightClass=\"65\""));
            }
        }

        [Test]
        public void WithNoFreightClassTheLogisticsPartnerIsAskedForTheClassThePlantPageUses()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LtlReplies(c);
            JObject body = LtlRequest(c);
            body.Remove("freightClass");

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                Post(body, _consumer.ApiKey);

                Assert.That(_carriers.Requests.Single(r => r.Url.Contains(LtlHost)).Body, Does.Contain("freightClass=\"65\""));
            }
        }

        [Test]
        public void EachPackageWeightInTheRequestReachesTheLogisticsPartner()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LtlReplies(c);
            JObject body = LtlRequest(c);
            body["packages"] = new JArray(new JObject { ["weight"] = 31 }, new JObject { ["weight"] = 17 }, new JObject { ["weight"] = 9 });

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                Post(body, _consumer.ApiKey);

                string sent = _carriers.Requests.Single(r => r.Url.Contains(LtlHost)).Body;
                Assert.That(System.Text.RegularExpressions.Regex.Matches(sent, "<Weight units=\"lb\">([0-9.]+)</Weight>").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value),
                    Is.EqualTo(new[] { "31", "17", "9" }));
            }
        }

        [Test]
        public void TheLtlCalculatedRateIsLoggedWithTheConsumerAsRequester_NotTbd()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LtlReplies(c);

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                int streamBefore = _consumer.LtlStreamRows();
                JObject quote = JObject.Parse(Body(Post(LtlRequest(c), _consumer.ApiKey)));

                var logged = _consumer.Calculations(Guid.Parse((string)quote["quoteId"])).Single();
                Assert.That(logged.Carrier, Is.EqualTo("LogisticsPartner"));
                Assert.That(logged.ServiceName, Is.EqualTo("LTL"));
                Assert.That(logged.UserName, Is.EqualTo(_consumer.Name));
                Assert.That(logged.CalculatedRate, Is.EqualTo((decimal)quote["services"][0]["calculatedRate"]));
                Assert.That(logged.CarrierRate, Is.EqualTo((decimal)quote["services"][0]["carrierRate"]));

                // The Logistics Partner's own request/response stream names the Consumer too, where the Plant page writes "TBD".
                Assert.That(_consumer.LtlStreamRows(), Is.EqualTo(streamBefore + 1));
            }
        }

        [Test]
        public void LtlIsNotAvailableUntilItIsSwitchedOn_AndTheLogisticsPartnerIsNotCalled()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            LtlReplies(c);

            JObject quote = JObject.Parse(Body(Post(LtlRequest(c), _consumer.ApiKey)));

            Assert.That((string)quote["services"][0]["availability"], Is.EqualTo("notAvailable"));
            Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
            Assert.That(_carriers.Requests, Is.Empty);
        }

        [Test]
        public void ALogisticsPartnerTimeoutIsA200WithThatCarrierUnavailable_AndTheUpsResultsAreKept()
        {
            QuoteCase ltl = QuoteCase.All("ltl").First();
            QuoteCase parcel = QuoteCase.All("parcel").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Shop", HttpStatusCode.OK, parcel.ResponseText).TimeOut(LtlHost);
            JObject body = GroundFreightRequest(parcel);
            body["plantCode"] = ltl.Plant;
            body["services"] = new JArray("UPSGround", "LTL");

            using (ServiceCatalog.Use("UPSGround", "LTL"))
            using (new SandboxPlantCharges(ltl.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                HttpResponseMessage response = Post(body, _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]),
                    Is.EquivalentTo(new[] { "UPS:ok", "LogisticsPartner:unavailable" }));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Not.Null, "UPS was still quoted");
                Assert.That((string)quote["services"][1]["availability"], Is.EqualTo("available"));
                Assert.That(quote["services"][1]["calculatedRate"], Is.Null);
                Assert.That((string)quote["carriers"].Single(k => (string)k["carrier"] == "LogisticsPartner")["message"], Is.EqualTo("The Logistics Partner did not answer in time."));
                Assert.That(_consumer.Quotes(), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void AUpsFailureLeavesTheLtlResultAlone()
        {
            QuoteCase ltl = QuoteCase.All("ltl").First();
            QuoteCase parcel = QuoteCase.All("parcel").First();
            _carriers.Replay("security", "ups-token-response.json").Replay("addressvalidation", "ups-address-validation-commercial.json")
                .TimeOut("rating/v2403/Shop").RespondWith(LtlHost, HttpStatusCode.OK, ltl.ResponseText);
            JObject body = LtlRequest(ltl);
            body["packages"] = new JArray(new JObject { ["weight"] = 20 }, new JObject { ["weight"] = 20 });   // UPS parcel rates at most 50 packages
            body["services"] = new JArray("UPSGround", "LTL");

            using (ServiceCatalog.Use("UPSGround", "LTL"))
            using (new SandboxPlantCharges(ltl.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                JObject quote = JObject.Parse(Body(Post(body, _consumer.ApiKey)));

                Assert.That(quote["carriers"].Select(k => (string)k["carrier"] + ":" + (string)k["status"]),
                    Is.EquivalentTo(new[] { "UPS:unavailable", "LogisticsPartner:ok" }));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
                Assert.That(quote["services"][1]["calculatedRate"], Is.Not.Null);
            }
        }

        [Test]
        public void ALogisticsPartnerErrorReplyIsA200WithThatCarrierInError()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            _carriers.RespondWith(LtlHost, HttpStatusCode.InternalServerError, "oops");

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                HttpResponseMessage response = Post(LtlRequest(c), _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("error"));
                Assert.That(quote["services"][0]["calculatedRate"], Is.Null);
                Assert.That(_consumer.Calculations(Guid.Parse((string)quote["quoteId"])), Is.Empty);
            }
        }

        [Test]
        public void ALogisticsPartnerReplyThatCannotBeUsedIsA200WithThatCarrierInError_NotA500()
        {
            QuoteCase c = QuoteCase.All("ltl").First();
            _carriers.RespondWith(LtlHost, HttpStatusCode.OK, "<not-the-reply-we-expect/>");

            using (ServiceCatalog.Use("LTL"))
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            {
                HttpResponseMessage response = Post(LtlRequest(c), _consumer.ApiKey);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                JObject quote = JObject.Parse(Body(response));
                Assert.That((string)quote["carriers"][0]["status"], Is.EqualTo("error"));
                Assert.That((string)quote["carriers"][0]["message"], Is.EqualTo("The Logistics Partner returned a reply that could not be used."));
                Assert.That(_consumer.Quotes(), Has.Count.EqualTo(1), "the Quote is still logged");
            }
        }

        // ---- helpers --------------------------------------------------------------------------------------------

        private void UpsParcelReplies(QuoteCase c) =>
            _carriers.Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Shop", HttpStatusCode.OK, c.ResponseText);

        private static decimal MarkupFor(SandboxPlantCharges.Row row, string service)
        {
            switch (service)
            {
                case "UPSGround": return row.Ground;
                case "UPS3DaySelect": return row.ThreeDaySelect;
                case "UPS2ndDayAir": return row.SecondDayAir;
                case "SecondDayAirAM": return row.SecondDayAirAM;
                case "NextDayAirSaver": return row.NextDayAirSaver;
                case "UPSNextDayAir": return row.NextDayAir;
                case "NextDayAirEarlyAM": return row.NextDayAirEarlyAM;
                default: throw new ArgumentException(service);
            }
        }

        private static string ServiceNameOf(string code)
        {
            switch (code)
            {
                case "03": return "UPSGround";
                case "12": return "UPS3DaySelect";
                case "02": return "UPS2ndDayAir";
                case "59": return "SecondDayAirAM";
                case "13": return "NextDayAirSaver";
                case "01": return "UPSNextDayAir";
                case "14": return "NextDayAirEarlyAM";
                default: return code;
            }
        }

        private static string LtlHost => new Uri(AuthenticationServer.Models.Configuration.TransportInsightRateUrl).Host;

        private void LtlReplies(QuoteCase c) => _carriers.RespondWith(LtlHost, HttpStatusCode.OK, c.ResponseText);

        /// <summary>A Quote request for what a recorded LTL case shipped, asking for LTL, at the case's freight class.</summary>
        private static JObject LtlRequest(QuoteCase c)
        {
            JObject body = GroundFreightRequest(c);
            body["services"] = new JArray("LTL");
            body["freightClass"] = c.FreightClass;
            return body;
        }

        private void UpsReplies(QuoteCase c) =>
            _carriers.Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Rate", HttpStatusCode.OK, c.ResponseText);

        /// <summary>A Quote request for what a recorded Ground Freight case shipped, packages listed one by one.</summary>
        private static JObject GroundFreightRequest(QuoteCase c)
        {
            var packages = new JArray();
            for (int i = 0; i < c.Packages - 1; i++) packages.Add(new JObject { ["weight"] = c.PackageWeight });
            packages.Add(new JObject { ["weight"] = c.LastPackageWeight > 0 ? c.LastPackageWeight : c.PackageWeight });

            JObject body = ValidRequest();
            body["plantCode"] = c.Plant;
            body["destination"] = new JObject { ["address"] = "1 Main St", ["city"] = "Anytown", ["state"] = c.ShipToState, ["postalCode"] = c.ShipToZip, ["country"] = "US" };
            body["packages"] = packages;
            body["services"] = new JArray("UPSGroundFreight");
            return body;
        }

        private static JObject ValidRequest() => JObject.Parse(@"{
            'plantCode': 'ALP',
            'referenceNumber': 'JOB-1001',
            'destination': { 'address': '1 Main St', 'city': 'Pittsburgh', 'state': 'PA', 'postalCode': '15201', 'country': 'US' },
            'packages': [ { 'weight': 12.5 }, { 'weight': 8 } ],
            'services': [ 'UPSGround' ],
            'freightClass': 65
        }");

        /// <summary>Damages one field of a valid request. The field is named the way the 400 is expected to name it.</summary>
        private static void Break(JObject body, string field, string fault)
        {
            switch (field + "|" + fault)
            {
                case "referenceNumber|remove": body.Remove("referenceNumber"); break;
                case "referenceNumber|blank": body["referenceNumber"] = "   "; break;
                case "packages|remove": body.Remove("packages"); break;
                case "packages|empty": body["packages"] = new JArray(); break;
                case "packages[0].weight|zero": body["packages"][0]["weight"] = 0; break;
                case "packages[1].weight|negative": body["packages"][1]["weight"] = -3; break;
                case "packages[0].weight|remove": ((JObject)body["packages"][0]).Remove("weight"); break;
                case "plantCode|remove": body.Remove("plantCode"); break;
                case "plantCode|unknown": body["plantCode"] = "ZZZ"; break;
                case "services[0]|unknown": body["services"] = new JArray("Teleport"); break;
                case "destination.postalCode|remove": ((JObject)body["destination"]).Remove("postalCode"); break;
                case "destination.state|remove": ((JObject)body["destination"]).Remove("state"); break;
                default: throw new ArgumentException("no such fault: " + field + " " + fault);
            }
        }

        private HttpResponseMessage Get(string url, string key) =>
            _client.SendAsync(Authenticated(new HttpRequestMessage(HttpMethod.Get, url), key)).Result;

        private HttpResponseMessage Post(JObject body, string key) =>
            _client.SendAsync(Authenticated(
                new HttpRequestMessage(HttpMethod.Post, "api/v1/quotes") { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") },
                key)).Result;

        private static HttpRequestMessage Authenticated(HttpRequestMessage request, string key)
        {
            if (key != null) request.Headers.Add("X-Api-Key", key);
            return request;
        }

        private static string Body(HttpResponseMessage response) => response.Content.ReadAsStringAsync().Result;

        private static string[] ErrorFields(HttpResponseMessage response) =>
            JObject.Parse(Body(response))["errors"].Select(e => (string)e["field"]).ToArray();
    }
}
