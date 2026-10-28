using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;
using System.Net;

namespace FreightEstimator.Tests
{
    /// <summary>UPSRequest.GetToken run offline through the carrier HTTP seam.</summary>
    public class UPSTokenTests
    {
        [Test]
        public void ReturnsTheAccessTokenFromTheRecordedResponse()
        {
            var fake = new FakeCarrierHttp().Replay("security/v1/oauth/token", "ups-token-response.json");

            string token;
            using (fake.Install())
            {
                token = new UPSRequest().GetToken();
            }

            Assert.That(token, Is.EqualTo("synthetic-token-123"));
        }

        [Test]
        public void AsksUpsForAClientCredentialsTokenWithBasicAuth()
        {
            var fake = new FakeCarrierHttp().Replay("security/v1/oauth/token", "ups-token-response.json");

            using (fake.Install())
            {
                new UPSRequest().GetToken();
            }

            CarrierHttpRequest sent = fake.Requests.Single();
            Assert.That(sent.Url, Is.EqualTo(Configuration.UPSGenerateTokenURL));
            Assert.That(sent.ContentType, Is.EqualTo("application/x-www-form-urlencoded"));
            Assert.That(sent.Body, Is.EqualTo("grant_type=client_credentials"));
            string expectedCredentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Configuration.UPSClientId + ":" + Configuration.UPSClientSecret));
            Assert.That(sent.Headers["Authorization"], Is.EqualTo("Basic " + expectedCredentials));
            Assert.That(sent.Headers["x-merchant-id"], Is.EqualTo(Configuration.ShipFromShipperNumber));
        }

        [Test]
        public void ReturnsTheErrorMessageAsTheTokenWhenUpsRefusesTheCredentials()
        {
            // Today's behaviour: GetToken never throws; on any failure the exception message is returned in place of a token.
            var fake = new FakeCarrierHttp().RespondWith("security/v1/oauth/token", HttpStatusCode.Unauthorized, "{\"response\":{\"errors\":[{\"code\":\"10401\"}]}}");

            string token;
            using (fake.Install())
            {
                token = new UPSRequest().GetToken();
            }

            Assert.That(token, Is.EqualTo("The remote server returned an error: (401) Unauthorized."));
        }

        [Test]
        public void ReturnsTheErrorMessageAsTheTokenWhenUpsTimesOut()
        {
            var fake = new FakeCarrierHttp().TimeOut("security/v1/oauth/token");

            string token;
            using (fake.Install())
            {
                token = new UPSRequest().GetToken();
            }

            Assert.That(token, Is.EqualTo("The operation has timed out."));
        }
    }
}
