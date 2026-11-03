using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
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

        [SetUp]
        public void SetUp()
        {
            _consumer = new SandboxConsumer();
            _carriers = new FakeCarrierHttp();   // no responses set up: any carrier call throws
            _carrierScope = _carriers.Install();

            var config = new HttpConfiguration();
            AuthenticationServer.WebApiConfig.Register(config);
            config.EnsureInitialized();
            _client = new HttpClient(new HttpServer(config)) { BaseAddress = new Uri("http://localhost/") };
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
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

        // ---- helpers --------------------------------------------------------------------------------------------

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
