using AuthenticationServer.Models;
using AuthenticationServer.Models.Carrier.UPS;
using AuthenticationServer.Models.Data;
using AuthenticationServer.Models.Services;
using Microsoft.Ajax.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using System.Web.UI.WebControls;
using System.Xml.Linq;
using System.Xml.XPath;

namespace AuthenticationServer.Controllers
{
    public class PlantController : Controller
    {
        private string _upsRequest = string.Empty;
        private string _upsResponse = string.Empty;
        private DataTable _multiView;
        private DataTable _multiGroundFreight;
        Models.Services.RateResponse rateResponse = new Models.Services.RateResponse();

        public ActionResult Index(string loc, string address = null, string city = null, string state = null, string zip = null,
            int? packages = null, float? weight = null, float? lastWeight = null,
            bool includeLtl = false, bool liftgatePickup = false, bool liftgateDelivery = false,
            bool limitedAccessPickup = false, bool limitedAccessDelivery = false,
            bool residentialPickup = false, bool residentialDelivery = false,
            bool insidePickup = false, bool insideDelivery = false,
            bool sortAndSegregate = false, bool stopoffCharge = false,
            bool notifyBeforeDelivery = false)
        {
            ViewBag.Param1 = loc;            
            
            if(string.IsNullOrEmpty(loc) || !Configuration.PlantLocations.ContainsKey(loc.ToUpper()))
            {
                return View("Error");
            }

            ViewBag.AccountMessage = "Required for Shipments from ALP\r\nor \"Rate from Multiple Locations\"";            

            ViewBag.States = Geography.States();
            ViewBag.Countries = Geography.Countries();
            

            var plantName = Configuration.PlantLocations[loc.ToUpper()];            
            var model = new Models.ShipmentView { };

            // Setup default values for the shipment
            model.Plant = new Plant(loc.ToUpper());
            model.user_name = "TBD";
            model.PlantId = model.Plant.Id;
            model.PlantName = plantName;
            model.Country_selection = "United States";
            //model.pick_up_date = System.DateTime.Now.ToShortDateString();

            model.delivery_signature_required = ShipmentViewLists.DeliverySignatureRequired;
            model.multiple_location_rate = new List<SelectListItem> { new SelectListItem { Text = "No", Value = "No" }, new SelectListItem { Text = "Yes", Value = "Yes" } };
            model.include_ground_rate = new List<SelectListItem> { new SelectListItem { Text = "Yes", Value = "Yes" }, new SelectListItem { Text = "No", Value = "No" } };
            model.include_ltl_rate = new List<SelectListItem> { new SelectListItem { Text = "No", Value = "No" }, new SelectListItem { Text = "Yes", Value = "Yes" } };

            model.freight_class = new List<SelectListItem>
            {
                new SelectListItem { Text = "50", Value = "50"},
                new SelectListItem { Text = "55", Value = "55"},
                new SelectListItem { Text = "60", Value = "60"},
                new SelectListItem { Text = "65", Value = "65"},
                new SelectListItem { Text = "70", Value = "70"},
                new SelectListItem { Text = "77.5", Value = "77.5"},
                new SelectListItem { Text = "85", Value = "85"},
                new SelectListItem { Text = "92.5", Value = "92.5"},
                new SelectListItem { Text = "100", Value = "100"},
                new SelectListItem { Text = "110", Value = "110"},
                new SelectListItem { Text = "125", Value = "125"},
                new SelectListItem { Text = "150", Value = "150"},
                new SelectListItem { Text = "175", Value = "175"},
                new SelectListItem { Text = "200", Value = "200"},
                new SelectListItem { Text = "250", Value = "250"},
                new SelectListItem { Text = "300", Value = "300"},
                new SelectListItem { Text = "400", Value = "400"},
                new SelectListItem { Text = "500", Value = "500"},

            };
            // Sep 21, 2025 - Switching from FreightClass 55 to 85 per NMFC changes.
            // Sep 26, 2025 - Switching from FreightClass 85 to 65 per feedback.            
            model.freight_class_selected = 65;
            model.default_pickup_date = System.DateTime.Today;

            model.pick_up_date = System.DateTime.Today;

            model.delivery_signature_required_selection = "No";
            model.multiple_location_rate_selection = "No";
            model.include_ground_rate_selection = "No";
            model.include_ltl_rate_selection = "No";

            if (!string.IsNullOrEmpty(address)) model.Address = address;
            if (!string.IsNullOrEmpty(city)) model.City = city;
            if (!string.IsNullOrEmpty(state)) model.State_selection = state;
            if (!string.IsNullOrEmpty(zip)) model.Zip = zip;
            if (packages.HasValue && packages.Value > 0) model.number_of_packages = packages.Value;
            if (weight.HasValue && weight.Value > 0) model.package_weight = weight.Value;
            if (lastWeight.HasValue && lastWeight.Value > 0) model.last_package_weight = lastWeight.Value;
            if (includeLtl) model.include_ltl_rate_selection = "Yes";
            if (liftgatePickup) model.liftgate_pickup = true;
            if (liftgateDelivery) model.liftgate_delivery = true;
            if (limitedAccessPickup) model.limited_access_pickup = true;
            if (limitedAccessDelivery) model.limited_access_delivery = true;
            if (residentialPickup) model.residential_pickup = true;
            if (residentialDelivery) model.residential_delivery = true;
            if (insidePickup) model.inside_pickup = true;
            if (insideDelivery) model.inside_delivery = true;
            if (sortAndSegregate) model.sort_and_segregate = true;
            if (stopoffCharge) model.stopoff_charge = true;
            if (notifyBeforeDelivery) model.notify_before_delivery = true;

            return View(model);
        }

        [HttpPost]
        public ActionResult SubmitShipment(ShipmentView shipment)
        {
            if (ModelState.IsValid)
            {
                if (shipment.Address.IsNullOrWhiteSpace())
                    shipment.Address = " ";
                // Save the shipment to the database
                return RedirectToAction("ShipmentConfirmation", shipment);
            }
            
            return View("Index", shipment);
        }

        public ActionResult LoadAdditionalOptions(Shipment shipment)
        {
            return PartialView("_AdditionalOptions", shipment);
        }

        public ActionResult ShipmentConfirmation(Shipment shipment)
        {
            shipment.Plant = new Plant(shipment.PlantId);
            shipment.QuoteId = Guid.NewGuid();

            string environmentName = Environment.GetEnvironmentVariable("ASPNET_ENVIRONMENT");
            if (string.IsNullOrEmpty(environmentName))
            {
                // Default to "Production" if the environment variable is not set
                environmentName = "Production";
            }
            ViewBag.Environment = environmentName;
            // Calculate billing weight
            shipment.billing_weight = UpsEstimationService.BillingWeight(shipment.number_of_packages, shipment.package_weight, shipment.last_package_weight);

            #region LOG REQUEST
            //rateResponse.Source = "UPS";
            rateResponse.PlantCode = shipment.PlantId;
            rateResponse.DestinationAddress1 = shipment.Address;
            rateResponse.DestinationAddress2 = string.Empty;
            rateResponse.DestinationAddress3 = string.Empty;
            rateResponse.DestinationCity = shipment.City;
            rateResponse.DestinationState = shipment.State_selection;
            rateResponse.DestinationPostalCode = shipment.Zip;
            rateResponse.Weight = (int)shipment.package_weight;
            rateResponse.LFTG_PU = shipment.liftgate_pickup;
            rateResponse.LFTG_D = shipment.liftgate_delivery;
            rateResponse.LAP = shipment.limited_access_delivery;
            rateResponse.LAPU = shipment.limited_access_pickup;
            rateResponse.RESD = shipment.residential_delivery;
            rateResponse.IND = shipment.inside_delivery;
            rateResponse.IPU = shipment.inside_pickup;
            rateResponse.SS = shipment.sort_and_segregate;
            rateResponse.STPO = shipment.stopoff_charge;

            
            #endregion

            shipment.requestMessage = _upsRequest;
            shipment.responseMessage = _upsResponse;

            // GRID 1 - Compare Rates
            if (shipment.number_of_packages <= UpsEstimationService.MaxShopPackages)  // UPS "Shop" has a 50 package limitation.
            {
                if (shipment.multiple_location_rate_selection == "Yes")
                {
                    List<UPSService> serviceComparison = new List<UPSService>();
                    List<ShopRateResponse> plantServices = new List<ShopRateResponse>();

                    // Get all of the plants and their service rates. (plantServices)
                    foreach (Plant plant in Plant.Plants())
                    {
                        Shipment shipmentResponse = new Shipment();
                        ShopRateResponse shopRate = new ShopRateResponse();
                        shipmentResponse = shipment;
                        shipmentResponse.PlantId = plant.Id;
                        shipmentResponse.PlantName = plant.Name;
                        shopRate = UpsEstimationService.CompareRates(shipmentResponse);

                        foreach (UPSService service in shopRate.UPSServices)
                        {
                            service.ShipFrom = shipment.PlantId;
                            //service.CustomerRate = RateCalculations.CalculateUPSRate(shipment.AcctNum, shipment.PlantId, service.ServiceName, service.CustomerRate, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()); // Should use CWT not ServiceName for cleanliness.
                            RateCalculations rateCalculations = new RateCalculations();
                            
                            switch (service.ServiceName)
                            {
                                case "UPSNextDayAir":                                    
                                    service.CWT = rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.UPSNextDayAir, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPS2ndDayAir":
                                    service.CWT = rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.UPS2ndDayAir, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSGround":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSGround, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSWorldwideExpress":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSWorldwideExpress, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSWorldwideExpedited":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSWorldwideExpedited, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSStandard":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSStandard, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPS3DaySelect":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPS3DaySelect, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "NextDayAirSaver":
                                    service.CWT = rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.NextDayAirSaver, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "NextDayAirEarlyAM":
                                    service.CWT = rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.NextDayAirEarlyAM, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "ExpressPlus":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.ExpressPlus, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "SecondDayAirAM":
                                    service.CWT = rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.SecondDayAirAM, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSSaver":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSSaver, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSTodayStandard":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSTodayStandard, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSTodayDedicatedCourier":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSTodayDedicatedCourier, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSTodayIntercity":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSTodayIntercity, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSTodayExpress":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSTodayExpress, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                                case "UPSTodayExpressSaver":
                                    service.CWT = rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSTodayExpressSaver, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
                                    break;
                            }

                            if (service.CWT.ToUpper() == "TRUE")
                            {
                                service.Plant_PerPackageCharge = rateCalculations.PerPackageChargeCWT[service.PlantCode];
                                service.Plant_ShipmentCharge = rateCalculations.PerShipmentChargeCWT[service.PlantCode];
                            }
                            else
                            {
                                service.Plant_PerPackageCharge = rateCalculations.PerPackageCharge[service.PlantCode];
                                service.Plant_ShipmentCharge = rateCalculations.PerShipmentCharge[service.PlantCode];
                            }

                            UpsEstimationService.LogCalculation(shipmentResponse, service);
                            ShadowComparisonRecorder.Record(
                                callSite: "GRID1Multi",
                                quoteId: shipment.QuoteId,
                                plantCode: service.PlantCode,
                                chargeScheduleServiceName: service.ServiceName,
                                displayServiceName: service.ServiceName,
                                packageCount: shipment.number_of_packages,
                                carrierRate: (decimal)service.CustomerRate,
                                isCwtEligible: service.CWT.ToUpper() == "TRUE",
                                existingCalculatedRate: (decimal)service.CustomerRate); // no markup is applied on this path today (the assignment is commented out), so the existing value is the raw carrier rate.
                        }

                        plantServices.Add(shopRate);

                    }
                    _multiView = MultiView(plantServices.ToArray());

                    ViewBag.MultiView = _multiView;

                    if (shipment.include_ground_rate_selection == "Yes")
                    {
                        DataTable dataTable = new DataTable();
                        dataTable.Columns.Add("Ship From");
                        dataTable.Columns.Add("Service");
                        dataTable.Columns.Add("Rate");

                        foreach (Plant plant in Plant.Plants())
                        {
                            Shipment gfShipment = new Shipment();
                            gfShipment = shipment;
                            gfShipment.PlantId = plant.Id;
                            ShopRateResponse shopRate = UpsEstimationService.GroundFreightRate(gfShipment);
                            if (shopRate.UPSServices.Length > 0)
                            {
                                dataTable.Rows.Add(plant.Id, "Ground Freight", shopRate.UPSServices[0].CustomerRate);
                            }
                        }
                        dataTable.Columns.Remove("CWT");
                        ViewBag.MultiGroundFreight = dataTable;
                    }
                }
            }


            // GRID 1 (single plant) and GRID 2 - UPS parcel and Ground Freight, quoted by the estimation service.
            // The multi-plant GRID 1 above still runs here.
            UpsQuoteResult upsQuote = new UpsEstimationService().Quote(new UpsQuoteRequest
            {
                PlantCode = shipment.PlantId,
                QuoteId = shipment.QuoteId,
                UserName = shipment.user_name,
                AccountNumber = shipment.AcctNum,
                Address = shipment.Address,
                City = shipment.City,
                State = shipment.State_selection,
                Zip = shipment.Zip,
                Country = shipment.Country_selection,
                NumberOfPackages = shipment.number_of_packages,
                PackageWeight = shipment.package_weight,
                LastPackageWeight = shipment.last_package_weight,
                IncludeParcel = shipment.multiple_location_rate_selection != "Yes",
                IncludeGroundFreight = shipment.include_ground_rate_selection == "Yes"
            });
            ApplyUpsOutcome(shipment, upsQuote.Outcome);

            if (upsQuote.ParcelCarrierRates != null)
            {
                #region LOG REQUEST
                List<string> services = new List<string>();
                List<string> carriers = new List<string>();
                List<string> rates = new List<string>();
                foreach (UpsCarrierRate carrierRate in upsQuote.ParcelCarrierRates)
                {
                    services.Add("UPS");
                    carriers.Add(carrierRate.ServiceName);
                    rates.Add(carrierRate.CarrierRate.ToString());
                }
                rateResponse.Service = services.ToArray();
                rateResponse.Carrier = carriers.ToArray();
                rateResponse.Rate = rates.ToArray();
                #endregion
            }

            if (upsQuote.Parcel != null)
            {
                shipment.shopCompareRates = new List<ShopRateResponse> { upsQuote.Parcel }.ToArray();
            }

            if (upsQuote.GroundFreight != null)
            {
                shipment.shopGroundFreightResponse = upsQuote.GroundFreight;
                #region
                // rateResponse.Service/Carrier/Rate are only populated by GRID 1 (skipped when
                // number_of_packages > 50), so they can still be null here; Enumerable.Append
                // also returns a new sequence rather than mutating in place, so the result must
                // be reassigned back.
                rateResponse.Service = (rateResponse.Service ?? Array.Empty<string>()).Append("UPS").ToArray();
                rateResponse.Carrier = (rateResponse.Carrier ?? Array.Empty<string>()).Append(shipment.shopGroundFreightResponse.UPSServices[0].ServiceName).ToArray();
                rateResponse.Rate = (rateResponse.Rate ?? Array.Empty<string>()).Append(shipment.shopGroundFreightResponse.UPSServices[0].CustomerRate.ToString()).ToArray();
                #endregion
            }

            // GRID 3 - LTL Rates
            if (shipment.include_ltl_rate_selection == "Yes")
            {
                //shipment.shopLessThanTruckloadResponseM33= GetLessThanTruckloadRates_M33(shipment);
                LogisticsPartnerQuoteResult ltlQuote = new LogisticsPartnerEstimationService().Quote(new LogisticsPartnerQuoteRequest
                {
                    PlantCode = shipment.PlantId,
                    QuoteId = shipment.QuoteId,
                    UserName = shipment.user_name,
                    AccountNumber = shipment.AcctNum,
                    CorrectedCity = shipment.Corrected_City,
                    State = shipment.State_selection,
                    Zip = shipment.Zip,
                    Country = shipment.Country_selection,
                    NumberOfPackages = shipment.number_of_packages,
                    PackageWeight = shipment.package_weight,
                    LastPackageWeight = shipment.last_package_weight,
                    FreightClass = shipment.freight_class_selected,
                    PickupDate = shipment.pick_up_date,
                    Accessorials = new LtlAccessorials
                    {
                        NotifyBeforeDelivery = shipment.notify_before_delivery,
                        LiftgatePickup = shipment.liftgate_pickup,
                        LiftgateDelivery = shipment.liftgate_delivery,
                        LimitedAccessPickup = shipment.limited_access_pickup,
                        LimitedAccessDelivery = shipment.limited_access_delivery,
                        ResidentialPickup = shipment.residential_pickup,
                        ResidentialDelivery = shipment.residential_delivery,
                        InsidePickup = shipment.inside_pickup,
                        InsideDelivery = shipment.inside_delivery,
                        SortAndSegregate = shipment.sort_and_segregate,
                        StopoffCharge = shipment.stopoff_charge
                    }
                });
                ApplyLogisticsPartnerOutcome(shipment, ltlQuote.Outcome);
                shipment.shopLessThanTruckloadResponseTransportInsight = ltlQuote.Rates;

                #region LOG REQUEST
                List<string> services = new List<string>();
                List<string> carriers = new List<string>();
                List<string> rates = new List<string>();
                foreach (UPSService uPSService in shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices)
                {
                    services.Add("UPS");
                    carriers.Add(uPSService.ServiceName);
                    rates.Add(uPSService.CustomerRate.ToString());
                }
                rateResponse.Service = services.ToArray();
                rateResponse.Carrier = carriers.ToArray();
                rateResponse.Rate = rates.ToArray();

                string[] newService = new string[rateResponse.Service.Length + shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices.Length];
                string[] newCarrier = new string[rateResponse.Carrier.Length + shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices.Length];
                string[] newRate = new string[rateResponse.Rate.Length + shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices.Length];
                rateResponse.Service.CopyTo(newService, 0);
                shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices.Select(x => x.ServiceName).ToArray().CopyTo(newService, rateResponse.Service.Length);
                rateResponse.Carrier.CopyTo(newCarrier, 0);
                shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices.Select(x => x.ServiceName).ToArray().CopyTo(newCarrier, rateResponse.Carrier.Length);
                rateResponse.Rate.CopyTo(newRate, 0);
                shipment.shopLessThanTruckloadResponseTransportInsight.UPSServices.Select(x => x.CustomerRate.ToString()).ToArray().CopyTo(newRate, rateResponse.Rate.Length);
                #endregion
            }


            ViewBag.plants = Plant.Plants();

            if (shipment.Address.IsNullOrWhiteSpace())
                shipment.Address_Classification = "N/A";

            // MORE INFORMATION
            ViewBag.PlantRates = Plant.Charges(shipment.PlantId);

            Log.LogRequest_Rate(rateResponse);

            

            return View(shipment);
        }

        /// <summary>
        /// The UPS estimation service quotes on its own copy of the shipment, so what the UPS request path learned
        /// (UPS's corrected address, Address Classification, errors and alerts, package defaults) comes back
        /// here and goes onto the page's shipment, which the rest of the page, including LTL, goes on to use.
        /// Null when the service made no UPS call. Corrections UPS did not make are left as they were.
        /// </summary>
        private static void ApplyUpsOutcome(Shipment shipment, UpsShipmentOutcome outcome)
        {
            if (outcome == null) return;

            shipment.Address = outcome.Address;
            shipment.City = outcome.City;
            shipment.State_selection = outcome.State;
            shipment.Zip = outcome.Zip;
            shipment.number_of_packages = outcome.NumberOfPackages;
            shipment.package_weight = outcome.PackageWeight;
            shipment.ErrorMessage = outcome.ErrorMessage;

            if (outcome.CorrectedAddress != null) shipment.Corrected_Address = outcome.CorrectedAddress;
            if (outcome.CorrectedCity != null) shipment.Corrected_City = outcome.CorrectedCity;
            if (outcome.CorrectedState != null) shipment.Corrected_State_selection = outcome.CorrectedState;
            if (outcome.AddressClassification != null) shipment.Address_Classification = outcome.AddressClassification;
            if (outcome.AlertMessages != null) shipment.AlertMessages = outcome.AlertMessages;
        }

        /// <summary>
        /// The Logistics Partner service quotes on its own copy of the shipment, so the defaults it applied (a blank
        /// account becomes "0", "US" becomes "USA") and the request and response it exchanged come back here and go
        /// onto the page's shipment, which the view shows. The messages are only taken when there are some.
        /// </summary>
        private static void ApplyLogisticsPartnerOutcome(Shipment shipment, LogisticsPartnerOutcome outcome)
        {
            shipment.AcctNum = outcome.AccountNumber;
            shipment.Country_selection = outcome.Country;
            if (outcome.RequestMessage != null) shipment.requestMessage = outcome.RequestMessage;
            if (outcome.ResponseMessage != null) shipment.responseMessage = outcome.ResponseMessage;
        }

        private void ValidateStateForCWT(Shipment shipment)
        {
            var CWTAirMinPkgs = Configuration.MinCWTPackagesAir;
            var CWTAirMinWeight = Configuration.MinCWTWeightAir;
            var CWTGroundMinPkgs = Configuration.MinCWTPackagesGround;
            var CWTGroundMinWeight = Configuration.MinCWTWeightGround;

            var StateIsValid = false;
            var IsHundredWeight = false;
            var NumberofPackages = shipment.number_of_packages;
            var PackageWeight = shipment.package_weight;
            var LastPackageWeight = shipment.last_package_weight;

            float TotalWeight = 0;

            if ((NumberofPackages != 0) && (PackageWeight != 0))
            {
                try
                {                    
                    if (LastPackageWeight != 0)
                    {
                        TotalWeight = (NumberofPackages - 1) * PackageWeight + LastPackageWeight;
                    }
                    else
                    {
                        TotalWeight = NumberofPackages * PackageWeight;
                    }

                    if(((NumberofPackages >= CWTAirMinPkgs) && (TotalWeight >= CWTAirMinWeight)) || ((NumberofPackages >= CWTGroundMinPkgs) && (TotalWeight >= CWTGroundMinWeight)))
                    {
                        IsHundredWeight = true;
                    }
                    else
                    {
                        IsHundredWeight = false;
                    }
                } catch (Exception ex)
                {
                    throw new Exception("Error calculating total weight: " + ex.Message);
                }
            }
            else
            {
                IsHundredWeight = false;
            }

            if (IsHundredWeight) {
                StateIsValid = (shipment.State_selection.Length >= 2);
            }
            else
            {
                StateIsValid = true;
            }
        }


        /// <summary>
        /// GRID 3 - Less Than Truckload (LTL) Rates
        /// Uses the Transplace API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private ShopRateResponse GetLessThanTruckloadRates_M33(Shipment shipment)
        {
            ShopRateResponse response = new ShopRateResponse();
            if (shipment.AcctNum.IsNullOrWhiteSpace()) shipment.AcctNum = "0";
            try
            {
                #region -- Define Per Package Charge and Per Shipment charge dictionaries for LTL (chare per plant) --
                Dictionary<string, double> dPerPackageChargeLTL = new Dictionary<string, double>();
                Dictionary<string, double> dPerShipmentChargeLTL = new Dictionary<string, double>();
                Dictionary<string, double> dUpchargeLTL = new Dictionary<string, double>();

                SqlConnection sqlConnection = new SqlConnection(Configuration.UpsRateSqlConnection);
                sqlConnection.Open();

                SqlCommand cmdCharges = sqlConnection.CreateCommand();
                cmdCharges.CommandText = "GetPlantCharges";
                cmdCharges.CommandType = System.Data.CommandType.StoredProcedure;
                cmdCharges.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = "M33";
                cmdCharges.Parameters.Add("@AcctNumber", System.Data.SqlDbType.Int).Value = shipment.AcctNum;

                SqlDataReader drCharges = cmdCharges.ExecuteReader();

                while (drCharges.Read())
                {
                    dPerPackageChargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["PerPackageCharge"].ToString()));
                    dPerShipmentChargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["PerShipmentCharge"].ToString()));
                    dUpchargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["Ground"].ToString()));
                }
                #endregion

                StringBuilder sbResults = new StringBuilder();
                string url = Configuration.TransPlaceUrl;
                string token = Configuration.TransPlaceToken;
                string fullPostData = "";
                string pickupDate = "";

                try
                {
                    DateTime datePickup = shipment.pick_up_date;
                    pickupDate = datePickup.Year.ToString() + "-" + datePickup.Month.ToString() + "-" + datePickup.Day.ToString();
                }
                catch
                {
                    pickupDate = DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString() + "-" + DateTime.Now.Day.ToString();
                }
                string ltlClass = shipment.freight_class_selected.ToString();

                #region -- Define dtLTLServices to hold rate data --
                DataSet dsLTLServices = new DataSet();
                DataTable dtLTLServices = dsLTLServices.Tables.Add();

                dtLTLServices.Columns.Add("Plant", typeof(string));
                dtLTLServices.Columns.Add("Service", typeof(string));
                dtLTLServices.Columns.Add("Rate", typeof(double));
                dtLTLServices.Columns.Add("TransitDays", typeof(int));
                dtLTLServices.Columns.Add("Direct", typeof(string));
                #endregion

                string[] plantCodes = { "" };

                if(shipment.PlantId == "ALL")
                {
                    plantCodes = Configuration.PlantCodesMultiRate;
                }
                else
                {
                    plantCodes[0] = shipment.PlantId;
                }

                string combinedResponses = "";
                List<UPSService> ltlServices = new List<UPSService>();
                string xmlResponse = string.Empty;

                foreach (string plantCode in plantCodes)
                {
                    CarrierHttpRequest request = new CarrierHttpRequest { Url = url + "rate/quote?loginToken=" + token };

                    #region -- Load Plant Ship From Address --
                    string shipFromCity = "";
                    string shipFromState = "";
                    string shipFromZip = "";
                    string shipFromCountry = "";

                    SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection);
                    conn.Open();

                    SqlCommand sqlCommand = conn.CreateCommand();
                    sqlCommand.Connection = conn;
                    sqlCommand.CommandType = CommandType.Text;
                    sqlCommand.CommandText = "SELECT city, State, Zip, Country FROM Plants WHERE Plantcode = '" + plantCode + "'";

                    SqlDataReader drResults = sqlCommand.ExecuteReader();

                    if (drResults.Read())
                    {
                        shipFromCity = drResults["City"].ToString();
                        shipFromState = drResults["State"].ToString();
                        shipFromZip = drResults["Zip"].ToString();
                        shipFromCountry = drResults["Country"].ToString();
                    }
                    else { 
                        sbResults.Append("Unable to lookup address info for Plant " + plantCode + "'");
                    }

                    conn.Close();

                    #endregion

                    #region Build XML Request
                    StringBuilder postData = new StringBuilder("<?xml version=\"1.0\"?>");
                    postData.Append("<quote>");
                    postData.Append("<requestedMode>LTL</requestedMode>");
                    postData.Append("<requestedPickupDate>" + System.DateTime.Now.ToString("yyyy-MM-dd") + "</requestedPickupDate>");
                    postData.Append("<shipper>");
                    postData.Append("<city>" + shipFromCity + "</city>");
                    postData.Append("<region>" + shipFromState + "</region>");
                    postData.Append("<country>" + shipFromCountry + "</country>");
                    postData.Append("<postalCode>" + shipFromZip + "</postalCode>");
                    postData.Append("</shipper>");
                    postData.Append("<consignee>");
                    postData.Append("<city>" + shipment.City + "</city>");
                    postData.Append("<region>" + shipment.State_selection + "</region>");
                    postData.Append("<country>" + shipment.Country_selection + "</country>");
                    postData.Append("<postalCode>" + shipment.Zip + "</postalCode>");
                    postData.Append("</consignee>");
                    postData.Append("<lineItems>");
                    postData.Append("<lineItem>");
                    postData.Append("<freightClass>" + ltlClass + "</freightClass>");
                    postData.Append("<weight>" + (shipment.package_weight * shipment.number_of_packages + shipment.last_package_weight) + "</weight>");
                    postData.Append("<weightUnit>LB</weightUnit>");
                    postData.Append("</lineItem>");
                    postData.Append("</lineItems>");

                    string accessorials = GetLTLAccessorials(shipment);
                    if (accessorials.Length > 0)
                    {
                        postData.Append("<accessorials>");
                        string[] accessorialArray = accessorials.Split(';');
                        for (int i = 0; i < accessorialArray.Count(); i++)
                        {
                            postData.Append("<accessorial><type>" + accessorialArray[i] + "</type></accessorial>");
                        }
                        postData.Append("</accessorials>");
                    }

                    postData.Append("</quote>");
                    #endregion
                    
                    fullPostData += postData;

                    request.ContentType = "text/xml";
                    request.Body = postData.ToString();

                    shipment.requestMessage = postData.ToString();

                    StringBuilder results = new StringBuilder();
                    results.Append("<div id=\"results\">");
                    results.Append("<table class=\"results-table\"><tr><td>").Append("<br/>" + url + "rate/quote?loginToken=" + token).Append("</td>");
                    results.Append("<td id=\"xmlRequest\">").Append(postData.ToString()).Append("</td></tr></table>");
                    // Get the response.
                    CarrierHttpResponse carrierResponse = CarrierHttp.Current.Post(request);
                    carrierResponse.EnsureSuccessStatus();
                    string responseFromServer = carrierResponse.Body;
                    // Display the content.
                    results.Append(responseFromServer);
                    results.Append("\n\n\n");

                    combinedResponses += responseFromServer;

                    shipment.responseMessage = responseFromServer;
                    xmlResponse = responseFromServer;
                    #region Parse response
                    XDocument xmlDoc = XDocument.Parse(responseFromServer);
                    

                    foreach (var rate in xmlDoc.Descendants("rate"))
                    {
                        string carrier = rate.Element("carrier").Element("name").Value.Trim();
                        string direct = rate.Element("direct").Value.Trim();
                        string transitDays = (rate.Element("transitDays").Value.Trim().Length > 0) ? rate.Element("transitDays").Value.Trim() : "-";
                        double originalBaseAmount = double.Parse(rate.Element("cost").Element("baseAmount").Value.Trim());
                        double originalFuelAmount = double.Parse(rate.Element("cost").Element("fuelAmount").Value.Trim());
                        double originalAccessorialAmount = double.Parse(rate.Element("cost").Element("accessorialAmount").Value.Trim());
                        double originalTotalAmount = double.Parse(rate.Element("cost").Element("totalAmount").Value.Trim());
                        double customerCost = double.Parse(rate.Element("cost").Element("totalAmount").Value.Trim());
                        double totalCharges = 0;
                        double plantSurcharge = 0;


                        #region -- Define variables for markup calculations --
                        double markupPercentage = 0;
                        double perPackageCharge = dPerPackageChargeLTL[plantCode];
                        double perShipmentCharge = dPerShipmentChargeLTL[plantCode];
                        #endregion

                        results.Append("Cost is " + customerCost.ToString() + "\n");
                        results.Append("Markup percentage is " + markupPercentage.ToString() + "\n");
                        results.Append("Number of Packages is " + shipment.number_of_packages + "\n");
                        results.Append("Per package charge is " + perPackageCharge.ToString() + "\n");
                        results.Append("Per shipment charge is " + perShipmentCharge.ToString() + "\n");

                        totalCharges = customerCost;
                        if (dUpchargeLTL[plantCode] > 0)
                        {
                            plantSurcharge = (dUpchargeLTL[plantCode] / 100);
                            totalCharges += (plantSurcharge * customerCost);
                        }
                        if (perPackageCharge > 0) { totalCharges += (perPackageCharge * shipment.number_of_packages); }
                        totalCharges += perShipmentCharge;

                        results.Append("Calculated total charge is " + totalCharges.ToString() + "\n\n");

                        if (carrier != "LTL BENCHMARK")
                        {
                            UPSService service = new UPSService();
                            service.PlantCode = plantCode;
                            service.ServiceName = carrier;
                            service.CustomerRate = customerCost;
                            service.TotalCost = totalCharges.ToString("C");

                            service.TransitDays = transitDays.ToString();
                            service.RatedShipment_Surcharge = (plantSurcharge * originalTotalAmount).ToString("C");
                            service.Plant_PerPackageCharge = (perPackageCharge * shipment.number_of_packages).ToString("C");
                            service.Plant_ShipmentCharge = perShipmentCharge.ToString("C");

                            service.RatedShipment_BaseServiceCharge_MonetaryValue = originalBaseAmount.ToString("C");
                            service.RatedShipment_TransportationCharges_MonetaryValue = originalFuelAmount.ToString("C");
                            service.RatedShipment_AccessorialCharges_MonetaryValue = originalAccessorialAmount.ToString("C");
                            service.RatedShipment_PublishedRateCharges_MonetaryValue = originalTotalAmount.ToString("C");

                            ltlServices.Add(service);

                        }
                        else if (Session["DefaultPlant"].ToString() == "POR")
                        {
                            UPSService service = new UPSService {
                                PlantCode = plantCode,
                                ServiceName = carrier,
                                CustomerRate = customerCost,
                                TransitDays = transitDays.ToString(),
                                Direct = direct,
                                RatedShipment_BaseServiceCharge_MonetaryValue = originalBaseAmount.ToString("C"),
                                RatedShipment_TransportationCharges_MonetaryValue = originalFuelAmount.ToString("C"),
                                RatedShipment_AccessorialCharges_MonetaryValue = originalAccessorialAmount.ToString("C"),
                                RatedShipment_PublishedRateCharges_MonetaryValue = totalCharges.ToString("C")
                            };

                            ltlServices.Add (service);
                        }

                        results.Append("</div>");
                        shipment.results = results.ToString();
                        response.UPSServices = ltlServices.ToArray();
                    }
                    #endregion
                }


                #region -- log results --
                try
                {
                    string UserName = "TBD";

                    SqlConnection conlog = new SqlConnection(Configuration.UpsRateSqlConnection);
                    conlog.Open();

                    SqlCommand cmdLog = new SqlCommand();
                    cmdLog.Connection = conlog;
                    cmdLog.CommandType = CommandType.StoredProcedure;
                    cmdLog.CommandText = "LogResultsLTL";

                    SqlParameter pPlantCode = new SqlParameter("@PlantCode", SqlDbType.VarChar, 10);
                    SqlParameter pUserName = new SqlParameter("@UserName", SqlDbType.VarChar, 10);
                    SqlParameter pFullRequest = new SqlParameter("@FullRequest", SqlDbType.NText);
                    SqlParameter pFullResults = new SqlParameter("@FullResults", SqlDbType.NText);
                    SqlParameter pXmlResponse = new SqlParameter("@XmlResponse", SqlDbType.NText);

                    pPlantCode.Value = shipment.PlantId;
                    pUserName.Value = UserName;
                    pFullRequest.Value = shipment.requestMessage;
                    pFullResults.Value = shipment.responseMessage;
                    pXmlResponse.Value = xmlResponse;

                    cmdLog.Parameters.Add(pPlantCode);
                    cmdLog.Parameters.Add(pUserName);
                    cmdLog.Parameters.Add(pFullRequest);
                    cmdLog.Parameters.Add(pFullResults);
                    cmdLog.Parameters.Add(pXmlResponse);

                    cmdLog.ExecuteNonQuery();
                    conlog.Close();
                }
                catch(Exception ex)
                {
                    throw ex;
                }
                #endregion
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return response;
        }

        
        private AddressKeyFormat AddressValidation(Shipment shipment)
        {
            UPSRequest uPSRequest = new UPSRequest();
            string addressValidationRequest = AddressValidationRequest(shipment);
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + uPSRequest.GetToken());

                try
                {
                    CarrierHttpRequest request = new CarrierHttpRequest
                    {
                        Url = Configuration.UPSAddressValidationURL,
                        ContentType = "application/json",
                        Body = addressValidationRequest
                    };
                    request.Headers["Authorization"] = "Bearer " + uPSRequest.GetToken();

                    // Get the response
                    CarrierHttpResponse response = CarrierHttp.Current.Post(request);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string result = response.Body;
                        UPSResponse uPSResponse = new UPSResponse();
                        AddressKeyFormat addressKeyFormat = new AddressKeyFormat();

                        dynamic data = JObject.Parse(result);

                        // Check for each key's existence and assign values accordingly
                        var candidateToken = data.SelectToken("XAVResponse.Candidate.AddressKeyFormat");
                        if (candidateToken != null)
                        {
                            addressKeyFormat.AddressLine = candidateToken.SelectToken("AddressLine")?.ToString() ?? "No Address Line";
                            addressKeyFormat.PoliticalDivision2 = candidateToken.SelectToken("PoliticalDivision2")?.ToString() ?? "No City";
                            addressKeyFormat.PoliticalDivision1 = candidateToken.SelectToken("PoliticalDivision1")?.ToString() ?? "No State";
                            addressKeyFormat.PostcodePrimaryLow = candidateToken.SelectToken("PostcodePrimaryLow")?.ToString() ?? "No Primary Zip Code";
                            addressKeyFormat.PostcodeExtendedLow = candidateToken.SelectToken("PostcodeExtendedLow")?.ToString() ?? "No Extended Zip Code";
                            addressKeyFormat.Region = candidateToken.SelectToken("Region")?.ToString() ?? "No Region";
                            addressKeyFormat.CountryCode = candidateToken.SelectToken("CountryCode")?.ToString() ?? "No Country Code";
                        }
                        else
                        {
                            Console.WriteLine("Candidate or AddressKeyFormat not found in the response.");
                        }

                        return addressKeyFormat;
                    }
                    else if (response.IsError)
                    {
                        Console.WriteLine("Error response JSON:");
                        Console.WriteLine(response.Body);
                    }
                    else
                    {
                        Console.WriteLine($"Error: {response.StatusCode} - {response.StatusDescription}");
                    }
                }
                catch (WebException ex)
                {
                    // No reply at all (timeout, connection failure): an error status comes back as a response above.
                    Console.WriteLine("WebException: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: " + ex.Message);
                }

                return null;

            }
        }
                
        private ShopRateResponse CalculateGroundFreight(List<PlantCharges> allPlantCharges, Shipment shipment)
        {
            ShopRateResponse shopRateResponse = new ShopRateResponse();
            // Iterate through plantShipment.shopRateResponse for the ground rates.
            List<UPSService> uPSService = new List<UPSService>();

            foreach (PlantCharges plantcharge in allPlantCharges)
            {
                // -- Process rate request for a single plant --
                try
                {
                    // -- Load Plant Ship From Address --
                    Plant plant = new Plant(plantcharge.PlantId);

                    Shipment plantShipment = shipment;
                    plantShipment.PlantId = plantcharge.PlantId;
                    plantShipment.PlantName = plant.Name;
                    plantShipment.shopCompareRates = new List<ShopRateResponse> { UpsEstimationService.CompareRates(plantShipment) }.ToArray();
                    // -- log request --
                    try
                    {
                        //Log(shipment.user_name, Configuration.UPSShopRatesURL, plant.Address, plant.City, plant.State, plant.Zip, plant.Country, plantShipment.requestMessage, plantShipment.responseMessage, ParameterDirection.Output);
                    }
                    catch (Exception e)
                    {
                        throw e;
                    }
                    // -- Process each rated service --


                    var rateResponse = UpsEstimationService.CompareRates(plantShipment);
                    var t = rateResponse.ToString();

                    foreach (var pShipment in rateResponse.UPSServices)
                    {
                        if (pShipment.ServiceName == "UPSGround")
                        {
                            uPSService.Add(pShipment);
                        }
                    }
                    shopRateResponse.UPSServices = uPSService.ToArray();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
           
            return shopRateResponse;

        }

        private DataTable MultiView(ShopRateResponse[] plantServices)
        { 
            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("carrier");
            dataTable.Columns.Add("ALP");
            dataTable.Columns.Add("BUT");
            dataTable.Columns.Add("FTW");
            dataTable.Columns.Add("POR");
            dataTable.Columns.Add("cwt");
            
            List<string> _alp_rate = new List<string>();
            List<string> _but_rate = new List<string>();
            List<string> _ftw_rate = new List<string>();
            List<string> _por_rate = new List<string>();

            List<string> supportedServices = new List<string>();
            supportedServices.Add("UPSGround");
            supportedServices.Add("UPS3DaySelect");
            supportedServices.Add("SecondDayAirAM");
            supportedServices.Add("NextDayAirSaver");
            supportedServices.Add("UPSNextDayAir");
            supportedServices.Add("NextDayAirEarlyAM");

            foreach (ShopRateResponse shopRateResponse in plantServices)
            {
                foreach (UPSService uPSService in shopRateResponse.UPSServices)
                {
                    switch (uPSService.PlantCode)
                    {
                        case "ALP":
                            _alp_rate.Add(uPSService.CustomerRate.ToString());
                            break;
                        case "BUT":
                            _but_rate.Add(uPSService.CustomerRate.ToString());
                            break;
                        case "FTW":
                            _ftw_rate.Add(uPSService.CustomerRate.ToString());
                            break;
                        case "POR":
                            _por_rate.Add(uPSService.CustomerRate.ToString());
                            break;
                    }
                }
            }

            int serviceCount = 0;
            foreach (string service in Configuration.UPSServiceCodeOrder)
            {
               if(supportedServices.Contains(service))
                {
                    dataTable.Rows.Add(service, _alp_rate[serviceCount], _but_rate[serviceCount], _ftw_rate[serviceCount], _por_rate[serviceCount], "TBD");
                    serviceCount++;
                }
            }
            
            return dataTable;
        }
        
        /// <summary>
        /// Builds the JSON address request for the UPS API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private string AddressValidationRequest(Shipment shipment)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("{\"XAVRequest\":");
            stringBuilder.Append("{\"AddressKeyFormat\": ");
            stringBuilder.Append("{\"ConsigneeName\": \"\",");
            stringBuilder.Append("\"BuildingName\": \"\",");
            stringBuilder.Append("\"AddressLine\": [\"" + shipment.Address + "\",\"\",\"\"],");  // Only using the first line of the array.
            stringBuilder.Append("\"Region\": \"" + shipment.City + "," + shipment.State_selection + "," + shipment.Zip + "\",");
            stringBuilder.Append("\"PoliticalDivision2\": \"\",");
            stringBuilder.Append("\"PoliticalDivision1\": \"" + shipment.State_selection + "\",");
            stringBuilder.Append("\"PostcodePrimaryLow\": \"" + shipment.Zip + "\",");
            stringBuilder.Append("\"PostcodeExtendedLow\": \"\",");
            stringBuilder.Append("\"Urbanization\": \"\",");
            stringBuilder.Append("\"CountryCode\": \"" + shipment.Country_selection + "\"");
            stringBuilder.Append("}"); // Consignee
            stringBuilder.Append("}"); // AddressKeyFormat
            stringBuilder.Append("}"); // ROOT

            return stringBuilder.ToString();
        }

        private string GetLTLAccessorials(Shipment shipment)
        {
            StringBuilder fullList = new StringBuilder();

            if(shipment.notify_before_delivery)
            {
                fullList.Append("NOTIFY-BEFORE-DELIVERY;");
            }
            if (shipment.liftgate_pickup)
            {
                fullList.Append("LIFTGATE-PICKUP;");
            }
            if (shipment.liftgate_delivery)
            {
                fullList.Append("LIFTGATE-DELIVERY;");
            }
            if (shipment.limited_access_pickup)
            {
                fullList.Append("LIMITED-ACCESS-PICKUP;");
            }
            if (shipment.limited_access_delivery)
            {
                fullList.Append("LIMITED-ACCESS-DELIVERY;");
            }
            if (shipment.residential_pickup)
            {
                fullList.Append("RESIDENTIAL-PICKUP;");
            }
            if (shipment.residential_delivery)
            {
                fullList.Append("RESIDENTIAL-DELIVERY;");
            }
            if (shipment.inside_pickup)
            {
                fullList.Append("INSIDE-PICKUP;");
            }
            if (shipment.inside_delivery)
            {
                fullList.Append("INSIDE-DELIVERY;");
            }
            if (shipment.sort_and_segregate)
            {
                fullList.Append("SORT-AND-SEGREGATE;");
            }
            if (shipment.stopoff_charge)
            {
                fullList.Append("STOPOFF-CHARGE;");
            }
            return fullList.ToString();
        }
        
    }
}
