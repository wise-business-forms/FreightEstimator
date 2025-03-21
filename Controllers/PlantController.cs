﻿using AuthenticationServer.Models;
using AuthenticationServer.Models.Services;
using Microsoft.Ajax.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Xml.Linq;

using AuthenticationServer.Models.Carrier.UPS;
using System.ComponentModel.DataAnnotations;
using System.Xml.XPath;
using System.Globalization;

namespace AuthenticationServer.Controllers
{
    public class PlantController : Controller
    {
        private string _upsRequest = string.Empty;
        private string _upsResponse = string.Empty;
        private DataTable _multiView;
        private DataTable _multiGroundFreight;

        public ActionResult Index(string loc)
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
            var model = new Models.Shipment { };

            // Setup default values for the shipment
            model.user_name = "TBD";
            model.PlantId = loc.ToUpper();
            model.PlantName = plantName;
            model.Country_selection = "United States";
            //model.pick_up_date = System.DateTime.Now.ToShortDateString();

            model.delivery_signature_required = Configuration.DeliverySignatureRequiredSelection;
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
            model.freight_class_selected = 55;
            model.default_pickup_date = System.DateTime.Today;

            model.pick_up_date = System.DateTime.Today;

            model.delivery_signature_required_selection = "No";
            model.multiple_location_rate_selection = "No";
            model.include_ground_rate_selection = "No";
            model.include_ltl_rate_selection = "No";

            return View(model);
        }

        [HttpPost]
        public ActionResult SubmitShipment(Shipment shipment)
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
            // Calculate billing weight 
            if (shipment.last_package_weight > 0)
            {
                shipment.billing_weight = (shipment.package_weight * (shipment.number_of_packages - 1)) + shipment.last_package_weight;
            }
            else { 
                shipment.billing_weight = shipment.number_of_packages * shipment.package_weight; 
            }
            
                
            shipment.requestMessage = _upsRequest;
            shipment.responseMessage = _upsResponse;

            // GRID 1 - Compare Rates
            if (shipment.number_of_packages <= 50)  // UPS "Shop" has a 50 package limitation.
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
                        shopRate = GetCompareRates(shipmentResponse);

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
                            ShopRateResponse shopRate = GetGroundFreightRate(gfShipment);
                            if (shopRate.UPSServices.Length > 0)
                            {
                                dataTable.Rows.Add(plant.Id, "Ground Freight", shopRate.UPSServices[0].CustomerRate);
                            }
                        }
                        dataTable.Columns.Remove("CWT");
                        ViewBag.MultiGroundFreight = dataTable;
                    }
                }
                else
                {
                    // Get carrier rates for plant
                    ShopRateResponse shopRateResponse = new ShopRateResponse();

                    // Returns rates from UPS API.
                    shopRateResponse = GetCompareRates(shipment);

                    // Apply plant charges.
                    List<PlantCharges> plantCharges = Plant.Charges(shipment.PlantId);
                    
                    if (shipment.ErrorMessage == "" || shipment.ErrorMessage == "Address not validated")
                    {
                        foreach (UPSService service in shopRateResponse.UPSServices)
                        {
                            service.ShipFrom = shipment.PlantId;                            
                            RateCalculations rateCalculations = new RateCalculations();
                            
                            //Apply plant surcharges.
                            PlantCharges charges = new PlantCharges();
                            UPSService _uPSService = null;

                            switch (service.ServiceName)
                            {
                                case "UPSGround": // Service Code: 03
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.UPSGround);
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;
                                    break;
                                case "UPS3DaySelect": // Service Code: 12
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.UPS3DaySelect);                                    
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;

                                    break;
                                case "UPS2ndDayAir": // Service Code: 02
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.UPS2ndDayAir);
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;
                                    break;
                                case "SecondDayAirAM": // Service Code: 59
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.SecondDayAirAM);
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;
                                    break;
                                case "NextDayAirSaver": // Service Code: 13
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.NextDayAirSaver);
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;
                                    break;
                                case "UPSNextDayAir": // Service Code: 01
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.UPSNextDayAir);
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;
                                    break;
                                case "NextDayAirEarlyAM": /// Service Code: 14
                                    _uPSService = ApplyPlantCharges(shipment, service, plantCharges, UPSService.ServiceCode.NextDayAirEarlyAM);
                                    service.CWT = _uPSService.CWT;
                                    service.ServiceName = _uPSService.ServiceName;
                                    service.Plant_CarrierId = _uPSService.Plant_CarrierId;
                                    service.Plant_Surcharge = _uPSService.Plant_Surcharge;
                                    service.Plant_PerPackageCharge = _uPSService.Plant_PerPackageCharge;
                                    service.Plant_ShipmentCharge = _uPSService.Plant_ShipmentCharge;
                                    break;
                            }
                        }
                        List<ShopRateResponse> shopRates = new List<ShopRateResponse> { shopRateResponse };
                        shipment.shopCompareRates = shopRates.ToArray();
                    }
                }
            }

            // GRID 2 - Ground Rate
            if (shipment.include_ground_rate_selection == "Yes") 
            { 
                shipment.shopGroundFreightResponse = GetGroundFreightRate(shipment);
                shipment.shopGroundFreightResponse.UPSServices[0].CustomerRate = RateCalculations.CalculateUPSRate(shipment.AcctNum, shipment.PlantId, "UPSGroundFreight", shipment.shopGroundFreightResponse.UPSServices[0].CustomerRate, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString());

                
            }

            // GRID 3 - LTL Rates
            if (shipment.include_ltl_rate_selection == "Yes")
            {
                //shipment.shopLessThanTruckloadResponseM33= GetLessThanTruckloadRates_M33(shipment);
                shipment.shopLessThanTruckloadResponseTransportInsight = GetLessThanTruckloadRates_TI(shipment);
            }

            


            ViewBag.plants = Plant.Plants();

            if (shipment.Address.IsNullOrWhiteSpace())
                shipment.Address_Classification = "N/A";

            // MORE INFORMATION
            ViewBag.PlantRates = Plant.Charges(shipment.PlantId);

            return View(shipment);
        }

        /// <summary>
        /// Accepts the base data and applies the appropriate surcharges, fees and markups.
        /// </summary>
        /// <param name="shipment"></param>
        /// <param name="uPSService"></param>
        /// <param name="plantCharges"></param>
        /// <param name="serviceCode"></param>
        /// <returns></returns>
        private UPSService ApplyPlantCharges(Shipment shipment, UPSService uPSService, List<PlantCharges> plantCharges, UPSService.ServiceCode serviceCode)
        {
            double markup = 0; // The calculated percentage based on markup_percentage.

            RateCalculations rateCalculations = new RateCalculations();
            PlantCharges charges = new PlantCharges();

            uPSService.CWT = rateCalculations.HundredWeightEligable(serviceCode, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString()).ToString();
            uPSService.ServiceName = serviceCode.ToString();
            uPSService.CWT_Adjustment = "0"; // Set default value.
            uPSService.Plant_Surcharge = "0"; // Set default vallue.

            // Since we are dealing with UPS only two carrier IDs matter.
            uPSService.Plant_CarrierId = uPSService.CWT.ToUpper() == "TRUE" ? "UPSCWT" : "UPS";

            uPSService.Plant_PerPackageCharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).PerPackageCharge.ToString();
            uPSService.Plant_ShipmentCharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).PerShipmentCharge.ToString();

            // Apply marke up per the carrier specified.
            switch (serviceCode)
            {
                case UPSService.ServiceCode.UPSGround:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).Ground.ToString());
                    break;
                case UPSService.ServiceCode.UPS3DaySelect:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).ThreeDaySelect.ToString());
                    break;
                case UPSService.ServiceCode.UPS2ndDayAir:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).SecondDayAir.ToString());
                    break;
                case UPSService.ServiceCode.SecondDayAirAM:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).SecondDayAirAM.ToString());
                    break;
                case UPSService.ServiceCode.NextDayAirSaver:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).NextDayAirSaver.ToString());
                    break;
                case UPSService.ServiceCode.UPSNextDayAir:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).NextDayAir.ToString());
                    break;
                case UPSService.ServiceCode.NextDayAirEarlyAM:
                    markup = Double.Parse(plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).NextDayAirEarlyAM.ToString());
                    break;
            }

            // TOTAL CUSTOMER RATE
            if (uPSService.ServiceName == "UPSGround")
            {
                uPSService.CustomerRate = Double.Parse(uPSService.RatedShipment_TotalCharges_MonetaryValue);               
            }
            else
            {
                uPSService.CustomerRate = Double.Parse(uPSService.RatedShipment_NegotiatedRateCharges_TotalCharge);
                uPSService.RatedShipment_TotalCharges_MonetaryValue = uPSService.RatedShipment_NegotiatedRateCharges_TotalCharge;

                // Apply CWT Adjustment only to negotiated services.
                if (uPSService.CWT.ToUpper() == "TRUE")
                {
                    uPSService.CWT_Adjustment = ((Double.Parse(uPSService.RatedShipment_NegotiatedRateCharges_TotalCharge) / 0.7) - Double.Parse(uPSService.RatedShipment_NegotiatedRateCharges_TotalCharge)).ToString();
                    uPSService.CustomerRate = uPSService.CustomerRate + Double.Parse(uPSService.CWT_Adjustment);
                }
            }

            // Set plant surcharge.
            if (markup > 0)
            {
                uPSService.Plant_Surcharge = ((markup / 100) * Double.Parse(uPSService.RatedShipment_TotalCharges_MonetaryValue)).ToString();
            }

            // Add final upcharges.
            uPSService.CustomerRate = uPSService.CustomerRate + 
                Double.Parse(uPSService.Plant_Surcharge) + 
                Double.Parse(uPSService.Plant_ShipmentCharge) + 
                (Double.Parse(uPSService.Plant_PerPackageCharge) * shipment.number_of_packages);

            return uPSService;
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
        /// GRID 1 - Compare Rates
        /// Note the "Shop" request option appended to the path.
        /// https://developer.ups.com/api/reference?loc=en_US#tag/Rating_other
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private ShopRateResponse GetCompareRates(Shipment shipment)
        {
            ShopRateResponse shopRateResponse = new ShopRateResponse();
            if (shipment.ErrorMessage == "" || shipment.ErrorMessage == null)
            {
                // Return negotiated rates.
                UPSRequest uPSRequestNegotiated = new UPSRequest(shipment, new Plant(shipment.PlantId), UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Negotiated);
                List<UPSService> uPSNegotiatedRates = uPSRequestNegotiated.UPSServices.ToList();

                // Return published rates.
                UPSRequest uPSRequestPublished = new UPSRequest(shipment, new Plant(shipment.PlantId), UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Published);
                List<UPSService> uPSPublishedRates = uPSRequestPublished.UPSServices.ToList();

                // Combine the results
                List<UPSService> combinedRates = new List<UPSService>();

                if (uPSPublishedRates != null)
                {
                    foreach (UPSService service in uPSPublishedRates)
                    {
                        if (service.ServiceName == "UPSGround") // Only Ground
                        {
                            combinedRates.Add(service);
                        }
                    }
                }

                if (uPSNegotiatedRates != null)
                {
                    foreach (UPSService service in uPSNegotiatedRates)
                    {
                        if (service.ServiceName != "UPSGround")  // Include ALL services except Ground.
                        {
                            combinedRates.Add(service);
                        }
                    }
                }

                shopRateResponse.UPSServices = combinedRates.ToArray();
            }
            return shopRateResponse;
        }

        /// <summary>
        /// GRID 2 - Generate the Ground Freight Rates for each plant.
        /// Note the "Rate" request option appended to the path.
        /// https://developer.ups.com/api/reference?loc=en_US#tag/Rating_other
        /// </summary>
        /// <param name="shipment"></param>
        private ShopRateResponse GetGroundFreightRate(Shipment shipment)
        {
            ShopRateResponse shopRateResponse = new ShopRateResponse();
            //if (shipment.ErrorMessage == "" || shipment.ErrorMessage == null)
            {
                UPSRequest upsRequest = new UPSRequest(shipment, new Plant { Id = shipment.PlantId }, UPSRequest.RequestOption.Rate, UPSRequest.RateClassification.Published);
                shopRateResponse.UPSServices = upsRequest.UPSServices;
            }
            return shopRateResponse;
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
                    WebRequest request = WebRequest.Create(url + "rate/quote?loginToken=" + token);
                    request.Method = "POST";

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

                    byte[] byteArray = Encoding.UTF8.GetBytes(postData.ToString());
                    // Set the ContentType property of the WebRequest.
                    request.ContentType = "text/xml";
                    // Set the ContentLength property of the WebRequest.
                    request.ContentLength = byteArray.Length;
                    // Get the request stream.
                    Stream dataStream = request.GetRequestStream();
                    // Write the data to the request stream.
                    dataStream.Write(byteArray, 0, byteArray.Length);
                    // Close the Stream object.
                    dataStream.Close();

                    shipment.requestMessage = postData.ToString();

                    StringBuilder results = new StringBuilder();
                    results.Append("<div id=\"results\">");
                    results.Append("<table class=\"results-table\"><tr><td>").Append("<br/>" + url + "rate/quote?loginToken=" + token).Append("</td>");
                    results.Append("<td id=\"xmlRequest\">").Append(postData.ToString()).Append("</td></tr></table>");
                    // Get the response.
                    WebResponse WebResponse = request.GetResponse();
                    // Display the status.
                    //Console.WriteLine(((HttpWebResponse)WebResponse).StatusDescription);
                    // Get the stream containing content returned by the server.
                    dataStream = WebResponse.GetResponseStream();
                    // Open the stream using a StreamReader for easy access.
                    StreamReader reader = new StreamReader(dataStream);
                    // Read the content.
                    string responseFromServer = reader.ReadToEnd();
                    // Display the content.
                    results.Append(responseFromServer);
                    results.Append("\n\n\n");
                    // Clean up the streams.
                    reader.Close();
                    dataStream.Close();
                    WebResponse.Close();

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
                            service.Plant_Surcharge = (plantSurcharge * originalTotalAmount).ToString("C");
                            service.Plant_PerPackageCharge = (perPackageCharge * shipment.number_of_packages).ToString("C");
                            service.Plant_ShipmentCharge = perShipmentCharge.ToString("C");

                            service.RatedShipment_BaseServiceCharge_MonetaryValue = originalBaseAmount.ToString("C");
                            service.RatedShipment_TransportationCharges_MonetaryValue = originalFuelAmount.ToString("C");
                            service.RatedShipment_AccessorialCharges_MonetaryValue = originalAccessorialAmount.ToString("C");
                            service.RatedShipment_TotalCharges_MonetaryValue = originalTotalAmount.ToString("C");

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
                                RatedShipment_TotalCharges_MonetaryValue = totalCharges.ToString("C")
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

        /// <summary>
        /// GRID 3 - Less Than Truckload (LTL) Rates
        /// Uses the Transplace API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private ShopRateResponse GetLessThanTruckloadRates_TI(Shipment shipment)
        {
            float PlantUpcharges = GetPlantUpcharges(shipment, "TI");
            double plantSurcharge = 0;
            double plantPackageCharge = 0;
            double plantShipmentCharge = 0;
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
                if (dUpchargeLTL[shipment.PlantId] > 0)
                {
                    plantSurcharge = dUpchargeLTL[shipment.PlantId];
                    plantSurcharge = (plantSurcharge / 100);
                }

                if (dPerPackageChargeLTL[shipment.PlantId] > 0)
                {
                    plantPackageCharge = dPerPackageChargeLTL[shipment.PlantId];
                }

                if (dPerShipmentChargeLTL[shipment.PlantId] > 0)
                {
                    plantShipmentCharge = dPerShipmentChargeLTL[shipment.PlantId];
                }

                #endregion

                StringBuilder sbResults = new StringBuilder();
                string url = Configuration.TransportInsightRateUrl;
                string userid = Configuration.TransportInsightRateUserId;
                string password = Configuration.TransportInsightRateUserPassword;
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

                if (shipment.PlantId == "ALL")
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

                    if (shipment.Country_selection == "US")
                    {
                        shipment.Country_selection = "USA";
                    }

                    if (drResults.Read())
                    {
                        shipFromCity = drResults["City"].ToString();
                        shipFromState = drResults["State"].ToString();
                        shipFromZip = drResults["Zip"].ToString();
                        shipFromCountry = drResults["Country"].ToString().Replace("US", "USA");
                    }
                    else
                    {
                        sbResults.Append("Unable to lookup address info for Plant " + plantCode + "'");
                    }

                    conn.Close();

                    #endregion

                    #region Build XML Request
                    StringBuilder request = new StringBuilder();
                    request.Append("<?xml version=\"1.0\"?>");
                    request.Append("<service-request xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
                    request.Append("<service-id>XMLRating</service-id>");
                    request.Append("<request-id>123456789</request-id>");
                    request.Append("<data>");
                        request.Append("<RateRequest><RatingLevel isCompanyAccountNumber=\"true\">WISE03RATE</RatingLevel>");
                        request.Append("<Constraints>");
                            request.Append("<PaymentTerms>Prepaid</PaymentTerms>");

                            string accessorials = GetAccessorials(shipment);
                            if(accessorials.Length > 0)
                            {
                                request.Append("<ServiceFlags>");
                                request.Append(accessorials);
                                request.Append("</ServiceFlags>");
                            }
                            else
                            {
                                request.Append("<ServiceFlags />");
                            }
                            
                        request.Append("</Constraints>");
                        request.Append("<Items>");
                    if (shipment.last_package_weight > 0)
                    {
                        for (int i = 0; i < shipment.number_of_packages-1; i++)
                        {
                            request.Append("<Item sequence=\"1\" freightClass=\"");
                            request.Append("55");
                            request.Append("\">");
                            request.Append("<Weight units=\"lb\">");
                            request.Append(shipment.package_weight.ToString());
                            request.Append("</Weight>");
                            request.Append("<Dimensions length=\"5.0\" width=\"5.0\" height=\"5.0\" units=\"in\" /></Item>");
                        }

                        // Add last package
                        request.Append("<Item sequence=\"1\" freightClass=\"");
                        request.Append("55");
                        request.Append("\">");
                        request.Append("<Weight units=\"lb\">");
                        request.Append(shipment.last_package_weight.ToString());
                        request.Append("</Weight>");
                        request.Append("<Dimensions length=\"5.0\" width=\"5.0\" height=\"5.0\" units=\"in\" /></Item>");
                    }
                    else
                    {
                        for (int i = 0; i < shipment.number_of_packages; i++)
                        {
                            request.Append("<Item sequence=\"1\" freightClass=\"");
                            request.Append("55");
                            request.Append("\">");
                            request.Append("<Weight units=\"lb\">");
                            request.Append(shipment.package_weight.ToString());
                            request.Append("</Weight>");
                            request.Append("<Dimensions length=\"5.0\" width=\"5.0\" height=\"5.0\" units=\"in\" /></Item>");
                        }
                    }
                        
                        request.Append("</Items>");

                    request.Append("<Events>");
                        request.Append("<Event sequence=\"1\" type=\"Pickup\" date=\"").Append(shipment.pick_up_date.ToString("MM/dd/yyyy HH:mm")).Append("\">");
                        request.Append("<Location><City>").Append(shipFromCity).Append("</City><State>").Append(shipFromState).Append("</State><Zip>").Append(shipFromZip).Append("</Zip><Country>").Append(shipFromCountry).Append("</Country></Location>");
                        request.Append("</Event>");

                        request.Append("<Event sequence=\"2\" type=\"Drop\" date=\"").Append(shipment.pick_up_date.ToString("MM/dd/yyyy HH:mm")).Append("\">");
                        request.Append("<Location><City>").Append(shipment.Corrected_City).Append("</City><State>").Append(shipment.State_selection).Append("</State><Zip>").Append(shipment.Zip).Append("</Zip><Country>").Append(shipment.Country_selection).Append("</Country></Location>");
                        request.Append("</Event>");
                    request.Append("</Events>");

                    request.Append("</RateRequest></data></service-request>");
                    #endregion

                    #region Add UserID/Pass & encode.
                    StringBuilder postData = new StringBuilder();
                    postData.Append("userid=").Append($"{Uri.EscapeDataString(Configuration.TransportInsightRateUserId)}");
                    postData.Append("&password=").Append($"{Uri.EscapeDataString(Configuration.TransportInsightRateUserPassword)}");
                    postData.Append("&request=").Append($"{Uri.UnescapeDataString(request.ToString())}");
                    #endregion

                    fullPostData += postData;

                    WebRequest webRequest = WebRequest.Create(url);
                    webRequest.ContentType = "application/x-www-form-urlencoded";
                    webRequest.Method = "POST";

                    byte[] byteArray = Encoding.UTF8.GetBytes(postData.ToString());
                    webRequest.ContentLength = byteArray.Length;

                    Stream dataStream = webRequest.GetRequestStream();
                    dataStream.Write(byteArray, 0, byteArray.Length);
                    dataStream.Close();

                    shipment.requestMessage = postData.ToString();

                    StringBuilder results = new StringBuilder(postData.ToString());
                    results.Append("\n\n\n");
                    results.Append("<br/>" + url);
                    results.Append("\n\n\n");
                    // Get the response.
                    WebResponse WebResponse = webRequest.GetResponse();
                    // Display the status.
                    //Console.WriteLine(((HttpWebResponse)WebResponse).StatusDescription);
                    // Get the stream containing content returned by the server.
                    dataStream = WebResponse.GetResponseStream();
                    // Open the stream using a StreamReader for easy access.
                    StreamReader reader = new StreamReader(dataStream);
                    // Read the content.
                    string responseFromServer = reader.ReadToEnd();
                    // Display the content.
                    results.Append(responseFromServer);
                    results.Append("\n\n\n");
                    // Clean up the streams.
                    reader.Close();
                    dataStream.Close();
                    WebResponse.Close();

                    combinedResponses += responseFromServer;
                    xmlResponse = responseFromServer;
                    shipment.responseMessage = responseFromServer;

                    #region Parse response
                    // Decode response.
                    XDocument xmlDoc = XDocument.Parse(responseFromServer);
                    string encodedData = xmlDoc.Descendants("data").FirstOrDefault()?.Value;
                    byte[] data = Convert.FromBase64String(encodedData);
                    string decodedString = System.Text.Encoding.UTF8.GetString(data);

                    // Parse the result.
                    XDocument decodedXmlDoc = XDocument.Parse(decodedString);
                    var priceSheets = decodedXmlDoc.Descendants("PriceSheet").Select((pricesheet, index) => new
                    {
                        PriceSheetIndex = index + 1,
                        CarrierName = pricesheet.Element("CarrierName")?.Value,
                        Rate = pricesheet.Element("SubTotal")?.Value,
                        TotalCost = pricesheet.Element("Total")?.Value,
                        TransitDays = pricesheet.Element("ServiceDays")?.Value,
                        FuelCharge = pricesheet
                            .Element("Charges")?
                            .Elements("Charge")
                            .FirstOrDefault(charge => (string)charge.Attribute("type") == "ACCESSORIAL_FUEL")
                            ?.Element("Amount")?.Value, // Adjust if you want a specific child node like <Amount>
                        TotalAccessorialAmount = pricesheet
                            .Element("Charges")?
                            .Elements("Charge")
                            .Where(charge => (string)charge.Attribute("type") == "ACCESSORIAL")
                            .Sum(charge => decimal.TryParse(charge.Element("Amount")?.Value, out var amount) ? amount : 0) // Sums all amounts
                        //AccessorialTotal = pricesheet.Element("AccessorialTotal")?.Value

                    });

                    foreach ( var priceSheet in priceSheets)
                    {
                        double totalCharges = Convert.ToDouble(priceSheet.TotalCost);

                        if(plantSurcharge > 0)
                        {
                            totalCharges += (plantSurcharge * Convert.ToDouble(priceSheet.TotalCost));
                        }

                        if(plantPackageCharge > 0)
                        {
                            totalCharges += (plantPackageCharge * shipment.number_of_packages);
                        }

                        totalCharges += plantShipmentCharge;

                        UPSService service = new UPSService();

                        service.PlantCode = plantCode;
                        service.ServiceName = priceSheet.CarrierName;
                        service.CustomerRate = double.Parse(priceSheet.TotalCost);
                        service.TotalCost = totalCharges.ToString("C");

                        service.TransitDays = priceSheet.TransitDays;
                        service.Plant_Surcharge = (plantSurcharge * Convert.ToDouble(priceSheet.TotalCost)).ToString("C");
                        service.Plant_PerPackageCharge = (plantPackageCharge * shipment.number_of_packages).ToString("C");
                        service.Plant_ShipmentCharge = plantShipmentCharge.ToString("C");

                        service.RatedShipment_BaseServiceCharge_MonetaryValue = priceSheet.Rate;
                        service.RatedShipment_TransportationCharges_MonetaryValue = double.Parse(priceSheet.FuelCharge).ToString("C");
                        service.RatedShipment_AccessorialCharges_MonetaryValue = priceSheet.TotalAccessorialAmount.ToString();
                        service.RatedShipment_TotalCharges_MonetaryValue = priceSheet.TotalCost;

                        ltlServices.Add(service);
                    }

                    response.UPSServices = ltlServices.ToArray();

                    try
                    {
                        response.UPSServices = response.UPSServices
    .Where(s => !string.IsNullOrWhiteSpace(s.TransitDays) && !string.IsNullOrWhiteSpace(s.RatedShipment_TotalCharges_MonetaryValue)) // Filter out invalid rows
    .OrderByDescending(s => float.TryParse(s.TransitDays, out float days) ? days : float.MinValue) // Parse TransitDays or use default for invalid values
    .ThenBy(s => float.TryParse(s.RatedShipment_TotalCharges_MonetaryValue, out float cost) ? cost : float.MinValue) // Parse TotalCharges or use default
    .ToArray();
                    }
                    catch(Exception ex)
                    {
                        // If there is a problem returnthe list unsorted.
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
                catch (Exception ex)
                {
                    throw ex;
                }
                #endregion
            }
            catch (WebException wex)
            {
                // Handle error response
                if (wex.Response is HttpWebResponse errorResponse)
                {
                    Console.WriteLine($"Error: {errorResponse.StatusCode}");
                    using (StreamReader reader = new StreamReader(errorResponse.GetResponseStream()))
                    {
                        string responseContent = reader.ReadToEnd();
                        Console.WriteLine("Response Body: ");
                        Console.WriteLine(responseContent);
                    }
                }
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
                    // Create HttpWebRequest
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Configuration.UPSAddressValidationURL);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + uPSRequest.GetToken());

                    // Write data to request stream
                    using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                    {
                        streamWriter.Write(addressValidationRequest);
                    }

                    // Get the response
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var streamReader = new StreamReader(response.GetResponseStream()))
                            {
                                string result = streamReader.ReadToEnd();
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
                        }
                        else
                        {
                            Console.WriteLine($"Error: {response.StatusCode} - {response.StatusDescription}");
                        }
                    }
                }
                catch (WebException ex)
                {
                    if (ex.Response != null)
                    {
                        using (var errorResponse = (HttpWebResponse)ex.Response)
                        {
                            using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                            {
                                string error = reader.ReadToEnd();
                                Console.WriteLine("Error response JSON:");
                                Console.WriteLine(error);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("WebException: " + ex.Message);
                    }
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
                    plantShipment.shopCompareRates = new List<ShopRateResponse> { GetCompareRates(plantShipment) }.ToArray();
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


                    var rateResponse = GetCompareRates(plantShipment);
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

        private float GetPlantUpcharges(Shipment shipment, String carrier)
        {
            Shipment _shipment = shipment;
            int accountNumber = 0;
            if (Int32.TryParse(shipment.AcctNum, out accountNumber))
            {
                accountNumber = Int32.Parse(shipment.AcctNum);
            }
            Dictionary<string, string> PerPackageCharge = new Dictionary<string, string>();
            SqlConnection sqlConnection = new SqlConnection(Configuration.UpsRateSqlConnection);
            sqlConnection.Open();

            SqlCommand sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "GetPlantCharges";
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = carrier.ToString();
            sqlCommand.Parameters.Add("AcctNumber", System.Data.SqlDbType.Int).Value = accountNumber;

            using (SqlDataReader reader = sqlCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    if(shipment.PlantId == reader["PlantCode"].ToString())
                    {
                        float pkgcharges = shipment.number_of_packages * float.Parse(reader["PerPackageCharge"].ToString());
                        float shipmentCharge = float.Parse(reader["PerShipmentCharge"].ToString());
                        float serviceCharge = float.Parse(reader["Ground"].ToString());
                        return pkgcharges + shipmentCharge + serviceCharge;
                    }
                }
            }
            return 0;
        }

        private string GetAccessorials(Shipment shipment) 
        { 
            StringBuilder accessorials = new StringBuilder();

            // NPDEL	Notification Prior to Delivery
            if (shipment.notify_before_delivery) { accessorials.Append("<ServiceFlag code=\"NPDEL\" />");  }
            // LIFT	Liftgate Service
            if (shipment.liftgate_pickup) { accessorials.Append("<ServiceFlag code=\"LIFT\" />"); }
            if (shipment.liftgate_delivery) { accessorials.Append("<ServiceFlag code=\"LIFT\" />"); }
            // LAPD	Limited Access Delivery
            if (shipment.limited_access_pickup) { accessorials.Append("<ServiceFlag code=\"LAPD\" />"); }
            if (shipment.limited_access_delivery) { accessorials.Append("<ServiceFlag code=\"LAPD\" />"); }
            // RESD	Residential Delivery
            if (shipment.residential_pickup) { accessorials.Append("<ServiceFlag code=\"RESD\" />"); }
            if (shipment.residential_delivery) { accessorials.Append("<ServiceFlag code=\"RESD\" />"); }
            // IDEL	Inside Delivery
            if (shipment.inside_pickup) { accessorials.Append("<ServiceFlag code=\"IDEL\" />"); }
            if (shipment.inside_delivery) { accessorials.Append("<ServiceFlag code=\"IDEL\" />"); }
            // ???
            if (shipment.sort_and_segregate) { accessorials.Append("<ServiceFlag code=\"SORT\" />"); }
            // ???
            if (shipment.stopoff_charge) { accessorials.Append("<ServiceFlag code=\"STOPOFF\" />"); } // ? Not sure this is correct based on https://qa-api-docs.mercurygate.net/documentation/standard-data.html

            return accessorials.ToString();
        }
        
    }
}
