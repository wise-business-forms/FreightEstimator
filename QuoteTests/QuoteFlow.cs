using AuthenticationServer.Controllers;
using AuthenticationServer.Models;
using FreightEstimator.Tests.Fakes;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Web.Mvc;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Runs one recorded quote through the Plant page's real flow (PlantController.ShipmentConfirmation)
    /// with UPS replaced by the carrier fake and SANDBOX holding a known charge schedule, and returns the
    /// Calculated Rate per service the page would show. The only seam is the controller action.
    /// </summary>
    public static class QuoteFlow
    {
        public static JObject Run(QuoteCase c, bool multiplePlants = false)
        {
            // GRID 1 always asks UPS "Shop"; GRID 2 (Ground Freight) additionally asks "Rate". For a Ground
            // Freight case the Shop call is answered with an unrelated parcel response and ignored.
            string shopResponse = c.Kind == "parcel"
                ? c.ResponseJson
                : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "ups-rate-response.json"));

            var fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Shop", HttpStatusCode.OK, shopResponse)
                .RespondWith("rating/v2403/Rate", HttpStatusCode.OK, c.ResponseJson);

            var shipment = new Shipment
            {
                PlantId = c.Plant,
                Address = "1 Main St",
                City = "Anytown",
                State_selection = c.ShipToState,
                Zip = c.ShipToZip,
                Country_selection = "US",
                number_of_packages = c.Packages,
                package_weight = c.PackageWeight,
                last_package_weight = c.LastPackageWeight,
                multiple_location_rate_selection = multiplePlants ? "Yes" : "No",
                include_ground_rate_selection = c.Kind == "groundfreight" ? "Yes" : "No",
                include_ltl_rate_selection = "No",
                AcctNum = "0",
                pick_up_date = DateTime.Today.AddDays(1)
            };

            ActionResult result;
            using (new SandboxPlantCharges(c.Plant, SandboxPlantCharges.KnownSchedule()))
            using (fake.Install())
            {
                result = new PlantController().ShipmentConfirmation(shipment);
            }

            var model = (Shipment)((ViewResult)result).Model;
            UPSService[] services = c.Kind == "parcel"
                ? model.shopCompareRates.SelectMany(r => r.UPSServices).ToArray()
                : model.shopGroundFreightResponse.UPSServices;

            var quoted = new JObject();
            foreach (UPSService s in services.OrderBy(s => s.ServiceName, StringComparer.Ordinal))
            {
                quoted[s.ServiceName] = new JObject
                {
                    ["calculatedRate"] = Math.Round(s.CustomerRate, 4),
                    ["cwt"] = s.CWT,
                    ["chargeSchedule"] = s.Plant_CarrierId,
                    ["markupPercent"] = s.Plant_Surcharge,
                    ["perPackageCharge"] = s.Plant_PerPackageCharge,
                    ["perShipmentCharge"] = s.Plant_ShipmentCharge
                };
            }
            return quoted;
        }
    }
}
