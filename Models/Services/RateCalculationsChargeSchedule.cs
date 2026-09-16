using System.Collections.Generic;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// Production ChargeSchedule adapter, wrapping the same Plant Charges data RateCalculations
    /// already loads (see docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md). Deliberately
    /// narrow: only reads what CalculatedRate.Apply needs, not a rewrite of RateCalculations'
    /// other database access.
    /// </summary>
    public class RateCalculationsChargeSchedule : IChargeSchedule
    {
        /// <summary>
        /// Routing sentinel for RatingContext.ServiceName: LTL has no per-service markup
        /// breakdown (unlike UPS), just one plant-level rate read from the "TI" carrier's Ground
        /// column, matching GetLessThanTruckloadRates_TI's own dPerPackageChargeLTL/
        /// dPerShipmentChargeLTL/dUpchargeLTL lookups. Not a real UPS service name.
        /// </summary>
        public const string LtlServiceName = "LTL";

        public PlantServiceCharges GetCharges(string plantCode, string serviceName, bool isCwtEligible)
        {
            if (serviceName == LtlServiceName)
            {
                RateCalculations ltlRateCalculations = new RateCalculations(0, RateCalculations.Carriers.TI);
                return new PlantServiceCharges
                {
                    MarkupPercent = LookUp(ltlRateCalculations.UpchargeGround, plantCode),
                    PerPackageCharge = LookUp(ltlRateCalculations.PerPackageCharge, plantCode),
                    PerShipmentCharge = LookUp(ltlRateCalculations.PerShipmentCharge, plantCode)
                };
            }

            RateCalculations rateCalculations = new RateCalculations(0, isCwtEligible ? RateCalculations.Carriers.UPSCWT : RateCalculations.Carriers.UPS);

            decimal perPackageCharge = LookUp(isCwtEligible ? rateCalculations.PerPackageChargeCWT : rateCalculations.PerPackageCharge, plantCode);
            decimal perShipmentCharge = LookUp(isCwtEligible ? rateCalculations.PerShipmentChargeCWT : rateCalculations.PerShipmentCharge, plantCode);
            decimal markupPercent = LookUp(UpchargeDictionaryFor(rateCalculations, serviceName, isCwtEligible), plantCode);

            return new PlantServiceCharges
            {
                MarkupPercent = markupPercent,
                PerPackageCharge = perPackageCharge,
                PerShipmentCharge = perShipmentCharge
            };
        }

        private static Dictionary<string, string> UpchargeDictionaryFor(RateCalculations rateCalculations, string serviceName, bool isCwtEligible)
        {
            switch (serviceName)
            {
                case "UPSGround":
                    return isCwtEligible ? rateCalculations.UpchargeGroundCWT : rateCalculations.UpchargeGround;
                case "UPS3DaySelect":
                    return isCwtEligible ? rateCalculations.UpchargeThreeDaySelectCWT : rateCalculations.UpchargeThreeDaySelect;
                case "UPSSaver":
                    return isCwtEligible ? rateCalculations.UpchargeSaverCWT : rateCalculations.UpchargeSaver;
                case "UPSNextDayAir":
                    return isCwtEligible ? rateCalculations.UpchargeNextDayAirCWT : rateCalculations.UpchargeNextDayAir;
                case "UPS2ndDayAir":
                    return isCwtEligible ? rateCalculations.UpchargeSecondDayAirCWT : rateCalculations.UpchargeSecondDayAir;
                case "NextDayAirSaver":
                    return isCwtEligible ? rateCalculations.UpchargeNextDayAirSaverCWT : rateCalculations.UpchargeNextDayAirSaver;
                case "NextDayAirEarlyAM":
                    return isCwtEligible ? rateCalculations.UpchargeNextDayAirEarlyAMCWT : rateCalculations.UpchargeNextDayAirEarlyAM;
                case "SecondDayAirAM":
                    return isCwtEligible ? rateCalculations.UpchargeSecondDayAirAMCWT : rateCalculations.UpchargeSecondDayAirAM;
                case "UPSGroundFreight":
                    return new RateCalculations(0, RateCalculations.Carriers.GF).UpchargeGround;
                default:
                    return new Dictionary<string, string>();
            }
        }

        private static decimal LookUp(Dictionary<string, string> charges, string plantCode)
        {
            if (charges != null && charges.TryGetValue(plantCode, out var value) && decimal.TryParse(value, out var result))
            {
                return result;
            }

            return 0m;
        }
    }
}
