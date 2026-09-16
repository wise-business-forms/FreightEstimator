using AuthenticationServer.Models.Services;
using AuthenticationServer.Models;
using System.Text.Json;
using AuthenticationServer.Models.Carrier.UPS;

namespace FreightEstimator.Tests
{
    public class UPSRequestTests
    {

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void UPSRequest()
        {
            AddressKeyFormat addressKeyFormat1 = new AddressKeyFormat
            {
                ConsigneeName = "George Washington",
                BuildingName = "Whitehouse",
                AddressLine = new string[] { "1600 Pennsylvania Ave NW" },
                Region = new string[] { "Washington, DC, 20500" },
                PoliticalDivision1 = "DC",
                PoliticalDivision2 = string.Empty,
                PostcodeExtendedLow = "123",
                PostcodePrimaryLow = "20500",
                Urbanization = string.Empty,
                CountryCode = "United States"
            };

            UPSRequest upsRequest = new UPSRequest
            {
                AddressKeyFormat = addressKeyFormat1
            };


            string json = upsRequest.ToJson();

            //Assert.IsFalse(string.IsNullOrEmpty(json), "The UPSRequest should not be null or empty.");

            if (upsRequest != null) {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    Assert.That(root.GetProperty("ConsigneeName").GetString(), Is.EqualTo("George Washington"), "CosigneeName property is incorrect.");
                    Assert.That(root.GetProperty("BuildingName").GetString(), Is.EqualTo("Whitehouse"), "BuildingName property is incorrect.");
                    Assert.That(root.GetProperty("Region").GetString(), Is.EqualTo("Washington, DC, 20500"), "Region property is incorrect.");
                    Assert.That(root.GetProperty("PoliticalDivision1").GetString(), Is.EqualTo("DC"), "PoliticalDivision1 property is incorrect.");
                    Assert.That(root.GetProperty("PoliticalDivision2").GetString(), Is.EqualTo(string.Empty), "PoliticalDivision2 property is incorrect.");
                    Assert.That(root.GetProperty("PostcodeExtendedLow").GetString(), Is.EqualTo("123"), "PostcodeExtendedLow property is incorrect.");
                    Assert.That(root.GetProperty("PostcodePrimaryLow").GetString(), Is.EqualTo("20500"), "PostcodePrimaryLow property is incorrect.");
                    Assert.That(root.GetProperty("Urbanization").GetString(), Is.EqualTo(string.Empty), "Urbanization property is incorrect.");
                    Assert.That(root.GetProperty("CountryCode").GetString(), Is.EqualTo("United States"), "CountryCode property is incorrect.");
                }
            }
            Assert.Pass();
        }
    }
}
