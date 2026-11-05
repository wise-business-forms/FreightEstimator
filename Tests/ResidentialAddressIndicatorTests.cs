using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;

namespace FreightEstimator.Tests
{
    public class ResidentialAddressIndicatorTests
    {
        private static Plant ButlerPlant() => new Plant
        {
            Id = "BUT",
            Name = "Butler",
            Address = "150 Kriess Road",
            City = "Butler",
            State = "PA",
            Zip = "16001",
            Country = "US"
        };

        private static Shipment AnnapolisShipment(string addressClassification) => new Shipment
        {
            PlantId = "BUT",
            AcctNum = "764534",
            Address = "2002 INDUSTRIAL DR",
            City = "ANNAPOLIS",
            State_selection = "MD",
            Zip = "21401-2942",
            Country_selection = "US",
            number_of_packages = 9,
            package_weight = 30,
            last_package_weight = 25,
            Address_Classification = addressClassification
        };

        [Test]
        public void IncludesResidentialAddressIndicatorWhenAddressIsClassifiedResidential()
        {
            UPSRequest upsRequest = new UPSRequest();

            string request = upsRequest.RateRequest(
                AnnapolisShipment("Residential"), ButlerPlant(),
                UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Negotiated);

            Assert.That(request, Does.Contain("\"ResidentialAddressIndicator\""));
        }

        [Test]
        public void OmitsResidentialAddressIndicatorWhenAddressIsClassifiedUnknown()
        {
            // This is the exact case from the Annapolis shipment this fix was found for:
            // UPS's own address database didn't know in advance, so there's nothing to pass through.
            UPSRequest upsRequest = new UPSRequest();

            string request = upsRequest.RateRequest(
                AnnapolisShipment("Unknown"), ButlerPlant(),
                UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Negotiated);

            Assert.That(request, Does.Not.Contain("ResidentialAddressIndicator"));
        }

        [Test]
        public void OmitsResidentialAddressIndicatorWhenAddressIsClassifiedCommercial()
        {
            UPSRequest upsRequest = new UPSRequest();

            string request = upsRequest.RateRequest(
                AnnapolisShipment("Commercial"), ButlerPlant(),
                UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Negotiated);

            Assert.That(request, Does.Not.Contain("ResidentialAddressIndicator"));
        }

        [Test]
        public void OmitsResidentialAddressIndicatorWhenClassificationIsNull()
        {
            UPSRequest upsRequest = new UPSRequest();

            string request = upsRequest.RateRequest(
                AnnapolisShipment(null), ButlerPlant(),
                UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Negotiated);

            Assert.That(request, Does.Not.Contain("ResidentialAddressIndicator"));
        }
    }
}
