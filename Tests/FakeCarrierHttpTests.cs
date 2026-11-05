using System.Net;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.Tests
{
    public class FakeCarrierHttpTests
    {
        private static CarrierHttpRequest Request(string url) =>
            new CarrierHttpRequest { Url = url, ContentType = "application/json", Body = "{}" };

        [Test]
        public void ReplaysARecordedUpsRateResponse()
        {
            var fake = new FakeCarrierHttp().Replay("onlinetools.ups.com/api/rating", "ups-rate-response.json");

            CarrierHttpResponse response = fake.Post(Request("https://onlinetools.ups.com/api/rating/v2403/Shop"));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Body, Does.Contain("\"RateResponse\""));
            Assert.That(response.Body, Does.Contain("\"RatedShipment\""));
        }

        [Test]
        public void ReplaysARecordedLogisticsPartnerResponse()
        {
            var fake = new FakeCarrierHttp().Replay("transportinsight", "logistics-partner-rate-response.xml");

            CarrierHttpResponse response = fake.Post(Request("https://rating.transportinsight.example/rate"));

            Assert.That(response.IsError, Is.False);
            Assert.That(response.Body, Does.Contain("<service-response>"));
        }

        [Test]
        public void SimulatesATimeoutAsAWebExceptionWithNoResponse()
        {
            var fake = new FakeCarrierHttp().TimeOut("onlinetools.ups.com");

            var ex = Assert.Throws<WebException>(() => fake.Post(Request("https://onlinetools.ups.com/api/rating/v2403/Shop")));

            Assert.That(ex!.Status, Is.EqualTo(WebExceptionStatus.Timeout));
            Assert.That(ex.Response, Is.Null);
        }

        [Test]
        public void SimulatesAnErrorStatusAsAResponseWithItsBody()
        {
            var fake = new FakeCarrierHttp().RespondWith("onlinetools.ups.com", HttpStatusCode.ServiceUnavailable, "{\"response\":{\"errors\":[{\"code\":\"500\"}]}}");

            CarrierHttpResponse response = fake.Post(Request("https://onlinetools.ups.com/api/rating/v2403/Shop"));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(response.IsError, Is.True);
            Assert.That(response.Body, Does.Contain("\"500\""));
        }

        [Test]
        public void RefusesARequestItWasNotToldAboutSoNothingReachesTheNetwork()
        {
            var fake = new FakeCarrierHttp().Replay("onlinetools.ups.com", "ups-rate-response.json");

            Assert.Throws<InvalidOperationException>(() => fake.Post(Request("https://somewhere.else.example/")));
        }

        [Test]
        public void InstallReplacesTheAppsCarrierHttpUntilDisposed()
        {
            ICarrierHttp original = CarrierHttp.Current;
            var fake = new FakeCarrierHttp();

            using (fake.Install())
            {
                Assert.That(CarrierHttp.Current, Is.SameAs(fake));
            }

            Assert.That(CarrierHttp.Current, Is.SameAs(original));
        }

        [Test]
        public void EnsureSuccessStatusThrowsTheSameWebExceptionTextAsTheFrameworkDoes()
        {
            var response = new CarrierHttpResponse { StatusCode = HttpStatusCode.Unauthorized, StatusDescription = "Unauthorized", Body = "{}" };

            var ex = Assert.Throws<WebException>(() => response.EnsureSuccessStatus());

            Assert.That(ex!.Message, Is.EqualTo("The remote server returned an error: (401) Unauthorized."));
            Assert.That(ex.Status, Is.EqualTo(WebExceptionStatus.ProtocolError));
        }

        [Test]
        public void EnsureSuccessStatusDoesNotThrowForStatusesGetResponseDidNotThrowFor()
        {
            // HttpWebRequest.GetResponse only threw for 4xx and 5xx, so 2xx and an unfollowed 3xx are not errors.
            foreach (HttpStatusCode status in new[] { HttpStatusCode.Created, HttpStatusCode.NotModified })
            {
                var response = new CarrierHttpResponse { StatusCode = status, StatusDescription = status.ToString(), Body = "" };
                Assert.DoesNotThrow(() => response.EnsureSuccessStatus(), status.ToString());
            }
        }

        [Test]
        public void EnsureSuccessStatusDoesNothingForASuccessfulResponse()
        {
            var response = new CarrierHttpResponse { StatusCode = HttpStatusCode.OK, StatusDescription = "OK", Body = "{}" };

            Assert.DoesNotThrow(() => response.EnsureSuccessStatus());
        }
    }
}
