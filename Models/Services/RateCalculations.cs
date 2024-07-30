using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models.Services
{
    public class RateCalculations
    {
        /// <summary>
        /// Calulates the Centum Weight (CWT) eligibility for all UPS air services.
        /// </summary>
        /// <param name="numberOfPackages"></param>
        /// <param name="packageWeight"></param>
        /// <param name="lastPackageWeight"></param>
        /// <returns></returns>
        public bool HundredWeightAirEligable(int numberOfPackages, string packageWeight, string lastPackageWeight)
        {
            if (HundredWeightEligable(UPSService.ServiceCode.NextDayAirEarlyAM, numberOfPackages, packageWeight, lastPackageWeight))
            {  return true; }
            else if (HundredWeightEligable(UPSService.ServiceCode.NextDayAirSaver, numberOfPackages, packageWeight, lastPackageWeight))
            { return true; }
            else if (HundredWeightEligable(UPSService.ServiceCode.SecondDayAirAM, numberOfPackages, packageWeight, lastPackageWeight))
            { return true; }
            else if (HundredWeightEligable(UPSService.ServiceCode.UPSNextDayAir, numberOfPackages, packageWeight, lastPackageWeight))
            { return true; }
            return false;
        }

        /// <summary>
        /// Calulates the Centum Weight (CWT) eligibility for all UPS ground services.
        /// </summary>
        /// <param name="numberOfPackages"></param>
        /// <param name="packageWeight"></param>
        /// <param name="lastPackageWeight"></param>
        /// <returns></returns>
        public bool HundredWeightGroundEligable(int numberOfPackages, string packageWeight, string lastPackageWeight)
        {
            if (HundredWeightEligable(UPSService.ServiceCode.UPSGround, numberOfPackages, packageWeight, lastPackageWeight))
            { return true; }
            else if (HundredWeightEligable(UPSService.ServiceCode.UPS3DaySelect, numberOfPackages, packageWeight, lastPackageWeight))
            { return true; }
            else if (HundredWeightEligable(UPSService.ServiceCode.UPSSaver, numberOfPackages, packageWeight, lastPackageWeight))
            { return true; }
            return false;
        }

        private bool HundredWeightEligable(UPSService.ServiceCode serviceCode, int numberOfPackages, string packageWeight, string lastPackageWeight)
        {
            bool _eligable = false;

            if (lastPackageWeight == null || lastPackageWeight == string.Empty || lastPackageWeight == "")
            {
                lastPackageWeight = packageWeight;
            }

            int _totalWeight = (Convert.ToInt16(packageWeight) * (numberOfPackages - 1)) + Convert.ToInt16(lastPackageWeight);

            switch (serviceCode)
            {
                case UPSService.ServiceCode.NextDayAirEarlyAM:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesAir) && (_totalWeight >= Configuration.MinCWTWeightAir);
                    break;
                case UPSService.ServiceCode.NextDayAirSaver:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesAir) && (_totalWeight >= Configuration.MinCWTWeightAir);
                    break;
                case UPSService.ServiceCode.SecondDayAirAM:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesAir) && (_totalWeight >= Configuration.MinCWTWeightAir);
                    break;
                case UPSService.ServiceCode.UPSNextDayAir:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesAir) && (_totalWeight >= Configuration.MinCWTWeightAir);
                    break;
                case UPSService.ServiceCode.UPSGround:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesGround) && (_totalWeight >= Configuration.MinCWTWeightGround);
                    break;
                case UPSService.ServiceCode.UPS3DaySelect:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesGround) && (_totalWeight >= Configuration.MinCWTWeightGround);
                    break;
                case UPSService.ServiceCode.UPSSaver:
                    _eligable = (numberOfPackages >= Configuration.MinCWTPackagesGround) && (_totalWeight >= Configuration.MinCWTWeightGround);
                    break;
                default:
                    break;
            }

            

            return _eligable;
        }
    }
}