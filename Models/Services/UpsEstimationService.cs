using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// What a caller asks the UPS estimation service for: one shipment from one Plant, and which UPS
    /// services it wants quoted. A plain request: the service never writes to it (see CONTEXT.md:
    /// Quote, Carrier Rate, Calculated Rate, Plant Charges).
    /// </summary>
    public class UpsQuoteRequest
    {
        /// <summary>The Plant the shipment ships from. Named explicitly; never inferred.</summary>
        public string PlantCode { get; set; }
        public Guid QuoteId { get; set; }

        /// <summary>Carried for the Quote API; persisted with the Quote by the Quote logging ticket (#18), not yet used.</summary>
        public string ReferenceNumber { get; set; }
        /// <summary>The Consumer that asked, or null for the Plant page. Persisted by #18, not yet used.</summary>
        public string Consumer { get; set; }
        public string UserName { get; set; }
        public string AccountNumber { get; set; }

        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string Country { get; set; }

        public int NumberOfPackages { get; set; }
        public float PackageWeight { get; set; }
        public float LastPackageWeight { get; set; }

        /// <summary>UPS parcel services (GRID 1, single plant).</summary>
        public bool IncludeParcel { get; set; }
        /// <summary>UPS Ground Freight (GRID 2).</summary>
        public bool IncludeGroundFreight { get; set; }
    }

    /// <summary>A UPS service's Carrier Rate as UPS returned it, before any Plant Charges.</summary>
    public class UpsCarrierRate
    {
        public string ServiceName { get; set; }
        public double CarrierRate { get; set; }
    }

    /// <summary>
    /// What the UPS request path learned about, or changed on, the shipment while quoting it: the corrected
    /// address, UPS's Address Classification, any error or alert, and the package defaults it applies. The
    /// caller decides what to do with them; the service does not write them back to the request.
    /// </summary>
    public class UpsShipmentOutcome
    {
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string CorrectedAddress { get; set; }
        public string CorrectedCity { get; set; }
        public string CorrectedState { get; set; }
        public string AddressClassification { get; set; }
        public string ErrorMessage { get; set; }
        public string[] AlertMessages { get; set; }
        public int NumberOfPackages { get; set; }
        public float PackageWeight { get; set; }
    }

    public class UpsQuoteResult
    {
        /// <summary>UPS parcel services with Plant Charges applied. Null when parcel was not asked for, was over the package limit, or UPS answered with an error.</summary>
        public ShopRateResponse Parcel { get; set; }
        /// <summary>The parcel Carrier Rates before Plant Charges, in UPS's order. Null when parcel was not requested or was over the package limit.</summary>
        public IReadOnlyList<UpsCarrierRate> ParcelCarrierRates { get; set; }
        /// <summary>UPS Ground Freight with its Calculated Rate. Null when not asked for.</summary>
        public ShopRateResponse GroundFreight { get; set; }
        /// <summary>What the UPS calls learned about the shipment. Null when no UPS call was made.</summary>
        public UpsShipmentOutcome Outcome { get; set; }
    }

    /// <summary>
    /// Quotes UPS parcel and Ground Freight for one shipment from one Plant. Extracted unchanged from
    /// PlantController.ShipmentConfirmation (#16): same UPS calls, same Calculated Rate formulas, same logging.
    /// The UPS request path writes to the shipment it is given (address, zip suffix, Address Classification,
    /// package defaults), so the service gives it a private working copy and reports the result in
    /// <see cref="UpsShipmentOutcome"/>.
    /// </summary>
    public class UpsEstimationService
    {
        /// <summary>UPS "Shop" rates at most this many packages.</summary>
        public const int MaxShopPackages = 50;

        // The services Plant Charges are applied to on the single-plant parcel path. Any other service UPS
        // returns keeps its carrier total and is only logged.
        private static readonly UPSService.ServiceCode[] ChargedParcelServices =
        {
            UPSService.ServiceCode.UPSGround,
            UPSService.ServiceCode.UPS3DaySelect,
            UPSService.ServiceCode.UPS2ndDayAir,
            UPSService.ServiceCode.SecondDayAirAM,
            UPSService.ServiceCode.NextDayAirSaver,
            UPSService.ServiceCode.UPSNextDayAir,
            UPSService.ServiceCode.NextDayAirEarlyAM
        };

        public UpsQuoteResult Quote(UpsQuoteRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Shipment shipment = WorkingShipment(request);
            var result = new UpsQuoteResult();
            bool askedUps = false;

            if (request.IncludeParcel && shipment.number_of_packages <= MaxShopPackages)
            {
                askedUps = true;
                QuoteParcel(shipment, result);
            }

            if (request.IncludeGroundFreight)
            {
                askedUps = true;
                result.GroundFreight = QuoteGroundFreight(shipment);
            }

            // Only a UPS call can change the shipment, so there is nothing to report otherwise.
            result.Outcome = askedUps ? Outcome(shipment) : null;
            return result;
        }

        /// <summary>Billing weight as the Plant page has always computed it.</summary>
        public static float BillingWeight(int numberOfPackages, float packageWeight, float lastPackageWeight)
        {
            return lastPackageWeight > 0
                ? (packageWeight * (numberOfPackages - 1)) + lastPackageWeight
                : numberOfPackages * packageWeight;
        }

        /// <summary>GRID 1 - Compare Rates. Note the "Shop" request option appended to the path.</summary>
        public static ShopRateResponse CompareRates(Shipment shipment)
        {
            ShopRateResponse shopRateResponse = new ShopRateResponse();
            if (shipment.ErrorMessage == "" || shipment.ErrorMessage == null)
            {
                // Return rates.
                List<UPSService> uPSRates = new UPSRequest(shipment, new Plant(shipment.PlantId), UPSRequest.RequestOption.Shop, UPSRequest.RateClassification.Negotiated).UPSServices.ToList();
                shopRateResponse.UPSServices = uPSRates.ToArray();
            }
            return shopRateResponse;
        }

        /// <summary>GRID 2 - Ground Freight. Note the "Rate" request option appended to the path.</summary>
        public static ShopRateResponse GroundFreightRate(Shipment shipment)
        {
            ShopRateResponse shopRateResponse = new ShopRateResponse();
            UPSRequest upsRequest = new UPSRequest(shipment, new Plant { Id = shipment.PlantId }, UPSRequest.RequestOption.Rate, UPSRequest.RateClassification.Negotiated);
            shopRateResponse.UPSServices = upsRequest.UPSServices;
            return shopRateResponse;
        }

        /// <summary>
        /// Persists a UPS service's Calculated Rate + markup breakdown for reporting. The try/catch here (not
        /// just inside Log.LogQuoteServiceCalculation) is deliberate: it also guards QuoteServiceCalculation.FromUps
        /// itself, so this can never break the live quote flow even if a future change to that factory starts throwing.
        /// </summary>
        public static void LogCalculation(Shipment shipment, UPSService service)
        {
            try
            {
                Log.LogQuoteServiceCalculation(QuoteServiceCalculation.FromUps(shipment, service));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("LogUpsQuoteServiceCalculation failed: " + ex.Message);
            }
        }

        private static Shipment WorkingShipment(UpsQuoteRequest request)
        {
            return new Shipment
            {
                PlantId = request.PlantCode,
                QuoteId = request.QuoteId,
                user_name = request.UserName,
                AcctNum = request.AccountNumber,
                Address = request.Address,
                City = request.City,
                State_selection = request.State,
                Zip = request.Zip,
                Country_selection = request.Country,
                number_of_packages = request.NumberOfPackages,
                package_weight = request.PackageWeight,
                last_package_weight = request.LastPackageWeight,
                billing_weight = BillingWeight(request.NumberOfPackages, request.PackageWeight, request.LastPackageWeight)
            };
        }

        private static UpsShipmentOutcome Outcome(Shipment shipment)
        {
            return new UpsShipmentOutcome
            {
                Address = shipment.Address,
                City = shipment.City,
                State = shipment.State_selection,
                Zip = shipment.Zip,
                CorrectedAddress = shipment.Corrected_Address,
                CorrectedCity = shipment.Corrected_City,
                CorrectedState = shipment.Corrected_State_selection,
                AddressClassification = shipment.Address_Classification,
                ErrorMessage = shipment.ErrorMessage,
                AlertMessages = shipment.AlertMessages,
                NumberOfPackages = shipment.number_of_packages,
                PackageWeight = shipment.package_weight
            };
        }

        private static void QuoteParcel(Shipment shipment, UpsQuoteResult result)
        {
            // Returns rates from UPS API.
            ShopRateResponse shopRateResponse = CompareRates(shipment);

            // The Carrier Rates as UPS returned them, before Plant Charges change CustomerRate.
            result.ParcelCarrierRates = shopRateResponse.UPSServices
                .Select(s => new UpsCarrierRate { ServiceName = s.ServiceName, CarrierRate = s.CustomerRate })
                .ToList();

            // Apply plant charges.
            List<PlantCharges> plantCharges = Plant.Charges(shipment.PlantId);

            if (shipment.ErrorMessage == "" || shipment.ErrorMessage == "Address not validated")
            {
                foreach (UPSService service in shopRateResponse.UPSServices)
                {
                    service.ShipFrom = shipment.PlantId;

                    // Apply plant surcharges.
                    // Exact name match, as the switch this replaced: Enum.TryParse would also accept numbers and any casing.
                    UPSService.ServiceCode serviceCode = ChargedParcelServices.FirstOrDefault(s => s.ToString() == service.ServiceName);
                    if (service.ServiceName == serviceCode.ToString())
                    {
                        UPSService charged = ApplyPlantCharges(shipment, service, plantCharges, serviceCode);
                        service.CWT = charged.CWT;
                        service.ServiceName = charged.ServiceName;
                        service.Plant_CarrierId = charged.Plant_CarrierId;
                        service.Plant_Surcharge = charged.Plant_Surcharge;
                        service.Plant_PerPackageCharge = charged.Plant_PerPackageCharge;
                        service.Plant_ShipmentCharge = charged.Plant_ShipmentCharge;
                    }

                    LogCalculation(shipment, service);
                }
                result.Parcel = shopRateResponse;
            }
        }

        private static ShopRateResponse QuoteGroundFreight(Shipment shipment)
        {
            ShopRateResponse groundFreight = GroundFreightRate(shipment);
            UPSService service = groundFreight.UPSServices[0];
            double groundFreightCarrierRate = service.CustomerRate;
            service.CustomerRate = RateCalculations.CalculateUPSRate(shipment.AcctNum, shipment.PlantId, "UPSGroundFreight", service.CustomerRate, shipment.number_of_packages, shipment.package_weight.ToString(), shipment.last_package_weight.ToString());

            RateCalculations r = new RateCalculations(0, RateCalculations.Carriers.GF);

            service.Plant_ShipmentCharge = r.UpchargeGround[shipment.PlantId];

            LogCalculation(shipment, service);
            ShadowComparisonRecorder.Record(
                callSite: "GRID2",
                quoteId: shipment.QuoteId,
                plantCode: shipment.PlantId,
                chargeScheduleServiceName: "UPSGroundFreight",
                displayServiceName: "UPSGroundFreight",
                packageCount: shipment.number_of_packages,
                carrierRate: (decimal)groundFreightCarrierRate,
                isCwtEligible: false, // CalculateUPSRate's own CWT branch is dead code, so GRID2 always behaves as non-CWT today.
                existingCalculatedRate: (decimal)service.CustomerRate);
            return groundFreight;
        }

        /// <summary>
        /// Accepts the base data and applies the appropriate surcharges, fees and markups.
        /// </summary>
        private static UPSService ApplyPlantCharges(Shipment shipment, UPSService uPSService, List<PlantCharges> plantCharges, UPSService.ServiceCode serviceCode)
        {
            uPSService.ServiceName = serviceCode.ToString();
            uPSService.CWT_Adjustment = "0"; // Set default value.
            uPSService.Plant_Surcharge = "0"; // Set default vallue.
            uPSService.RatedShipment_Surcharge = "0"; // Set default vallue.

            // Set the carrier ID based on whether or not it is CWT.  Since we are dealing with UPS only two carrier IDs matter.
            uPSService.Plant_CarrierId = uPSService.CWT.ToUpper() == "YES" ? "UPSCWT" : "UPS";

            uPSService.Plant_PerPackageCharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).PerPackageCharge.ToString();
            uPSService.Plant_ShipmentCharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).PerShipmentCharge.ToString();

            // Get plant surcharge...
            switch (serviceCode)
            {
                case UPSService.ServiceCode.UPSGround:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).Ground.ToString();
                    break;
                case UPSService.ServiceCode.UPS3DaySelect:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).ThreeDaySelect.ToString();
                    break;
                case UPSService.ServiceCode.UPS2ndDayAir:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).SecondDayAir.ToString();
                    break;
                case UPSService.ServiceCode.SecondDayAirAM:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).SecondDayAirAM.ToString();
                    break;
                case UPSService.ServiceCode.NextDayAirSaver:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).NextDayAirSaver.ToString();
                    break;
                case UPSService.ServiceCode.UPSNextDayAir:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).NextDayAir.ToString();
                    break;
                case UPSService.ServiceCode.NextDayAirEarlyAM:
                    uPSService.Plant_Surcharge = plantCharges.FirstOrDefault(pc => pc.CarrierId == uPSService.Plant_CarrierId).NextDayAirEarlyAM.ToString();
                    break;
            }

            // TOTAL CUSTOMER RATE
            // It was decided 3/21/2025 by all of the GMs that:
            // If UPS/FEDEX CWT use negotiated (that means <100 if air, > 200 if ground)
            // If not UPS / FEDEX CWT use published
            // Remove the (negotiated / .7) cwt weight adjustment.

            bool isCwtEligible = uPSService.CWT.ToUpper() == "TRUE" || uPSService.CWT.ToUpper() == "YES";
            double carrierRate = isCwtEligible
                ? Double.Parse(uPSService.RatedShipment_NegotiatedRateCharges_TotalCharge)
                : Double.Parse(uPSService.RatedShipment_PublishedRateCharges_MonetaryValue);

            if (isCwtEligible)
            {
                // MARKUP should be the NEGOTIATED rate multiplied by the surcharge expressed as a percentage.
                var _markup = carrierRate * (Double.Parse(uPSService.Plant_Surcharge) / 100);

                // The CUSTOMERRATE (customer cost) should be the NEGOTIATED rate plus the MARKUP.
                uPSService.CustomerRate = carrierRate + _markup;
            }
            else
            {
                uPSService.CustomerRate = carrierRate;
            }

            // Set plant surcharge.
            if (double.Parse(uPSService.Plant_Surcharge) > 0)
            {
                uPSService.RatedShipment_Surcharge = ((double.Parse(uPSService.Plant_Surcharge) / 100) * Double.Parse(uPSService.RatedShipment_NegotiatedRateCharges_TotalCharge)).ToString();
            }


            // Add final upcharges.
            uPSService.CustomerRate = uPSService.CustomerRate +
                Double.Parse(uPSService.Plant_ShipmentCharge) +
                (Double.Parse(uPSService.Plant_PerPackageCharge) * shipment.number_of_packages);

            ShadowComparisonRecorder.Record(
                callSite: "GRID1Single",
                quoteId: shipment.QuoteId,
                plantCode: shipment.PlantId,
                chargeScheduleServiceName: uPSService.ServiceName,
                displayServiceName: uPSService.ServiceName,
                packageCount: shipment.number_of_packages,
                carrierRate: (decimal)carrierRate,
                isCwtEligible: isCwtEligible,
                existingCalculatedRate: (decimal)uPSService.CustomerRate);

            return uPSService;
        }
    }
}
