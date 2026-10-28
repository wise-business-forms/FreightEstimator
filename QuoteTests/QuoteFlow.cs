using AuthenticationServer.Controllers;
using AuthenticationServer.Models;
using FreightEstimator.Tests.Fakes;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;
using System.Web.Mvc;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Runs one recorded quote through the Plant page's real flow (PlantController.ShipmentConfirmation)
    /// with the carriers replaced by the carrier fake and SANDBOX holding a known charge schedule, and
    /// returns what the page would show per service. The only seam is the controller action.
    /// </summary>
    public static class QuoteFlow
    {
        public static JObject Run(QuoteCase c, bool multiplePlants = false, SandboxPlantCharges.Row[] schedule = null)
        {
            // GRID 1 asks UPS "Shop" whenever there are 50 packages or fewer; GRID 2 (Ground Freight)
            // additionally asks "Rate". Cases that are not about GRID 1 answer Shop with an unrelated
            // parcel response that the test ignores.
            string shopResponse = c.Kind == "parcel"
                ? c.ResponseText
                : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "ups-rate-response.json"));

            var fake = new FakeCarrierHttp()
                .Replay("security", "ups-token-response.json")
                .Replay("addressvalidation", "ups-address-validation-commercial.json")
                .RespondWith("rating/v2403/Shop", HttpStatusCode.OK, shopResponse)
                .RespondWith("rating/v2403/Rate", HttpStatusCode.OK, c.ResponseText)
                .RespondWith(new Uri(AuthenticationServer.Models.Configuration.TransportInsightRateUrl).Host, HttpStatusCode.OK, c.ResponseText);

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
                freight_class_selected = c.FreightClass,
                multiple_location_rate_selection = multiplePlants ? "Yes" : "No",
                include_ground_rate_selection = c.Kind == "groundfreight" ? "Yes" : "No",
                include_ltl_rate_selection = c.Kind == "ltl" ? "Yes" : "No",
                AcctNum = "0",
                pick_up_date = new DateTime(2026, 11, 3)   // fixed, so a run never depends on the day it happens on
            };

            // The LTL total is shown as a currency string; fix the culture so it reads the same on every machine.
            CultureInfo previousCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            ActionResult result;
            try
            {
                using (new SandboxPlantCharges(c.Plant, schedule ?? SandboxPlantCharges.KnownSchedule()))
                using (fake.Install())
                {
                    result = new PlantController().ShipmentConfirmation(shipment);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
            }

            var model = (Shipment)((ViewResult)result).Model;
            return c.Kind == "ltl" ? Ltl(model) : Ups(c, model);
        }

        private static JObject Ups(QuoteCase c, Shipment model)
        {
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

        // LTL (Logistics Partner): CustomerRate is the carrier's own total and TotalCost is the Calculated
        // Rate the page shows. A carrier can appear more than once (direct and indirect), so key by name + order.
        private static JObject Ltl(Shipment model)
        {
            UPSService[] services = model.shopLessThanTruckloadResponseTransportInsight.UPSServices
                .OrderBy(s => s.ServiceName, StringComparer.Ordinal)
                .ThenBy(s => s.CustomerRate)
                .ToArray();

            var quoted = new JObject();
            var seen = new Dictionary<string, int>();
            foreach (UPSService s in services)
            {
                seen[s.ServiceName] = seen.TryGetValue(s.ServiceName, out int n) ? n + 1 : 1;
                quoted[s.ServiceName + " #" + seen[s.ServiceName]] = new JObject
                {
                    ["carrierRate"] = Math.Round(s.CustomerRate, 4),
                    ["calculatedRate"] = s.TotalCost,
                    ["markupAmount"] = s.RatedShipment_Surcharge,
                    ["perPackageCharge"] = s.Plant_PerPackageCharge,
                    ["perShipmentCharge"] = s.Plant_ShipmentCharge
                };
            }
            return quoted;
        }
    }
}
