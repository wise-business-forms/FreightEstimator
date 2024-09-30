using AuthenticationServer.Models.Carrier.UPS;
using Microsoft.Ajax.Utilities;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using System.Web.Razor.Generator;

namespace AuthenticationServer.Models.Services
{

    public class RateCalculations
    {
        #region Private vars
        private Dictionary<string, string> _PerPackageCharge = new Dictionary<string, string>();
        private Dictionary<string, string> _PerShipmentCharge = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeNextDayAir = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeSecondDayAir = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeGround = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeThreeDaySelect = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeNextDayAirSaver = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeNextDayAirEarlyAM = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeSecondDayAirAM = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeSaver = new Dictionary<string, string>();

        private Dictionary<string, string> _PerPackageChargeCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _PerShipmentChargeCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeNextDayAirCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeSecondDayAirCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeGroundCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeThreeDaySelectCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeNextDayAirSaverCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeNextDayAirEarlyAMCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeSecondDayAirAMCWT = new Dictionary<string, string>();
        private Dictionary<string, string> _UpchargeSaverCWT = new Dictionary<string, string>();
        #endregion

        public enum Carriers { UPS, UPSCWT, GF }

        public Dictionary<string, string> PerPackageCharge { get { return _PerPackageCharge; } }
        public Dictionary<string, string> PerShipmentCharge { get { return _PerShipmentCharge; } }
        public Dictionary<string, string> UpchargeNextDayAir { get { return _UpchargeNextDayAir; } }
        public Dictionary<string, string> UpchargeSecondDayAir { get { return _UpchargeSecondDayAir; } }
        public Dictionary<string, string> UpchargeGround { get { return _UpchargeGround; } }
        public Dictionary<string, string> UpchargeThreeDaySelect { get { return _UpchargeThreeDaySelect; } }
        public Dictionary<string, string> UpchargeNextDayAirSaver { get { return _UpchargeNextDayAirSaver; } }
        public Dictionary<string, string> UpchargeNextDayAirEarlyAM { get { return _UpchargeNextDayAirEarlyAM; } }
        public Dictionary<string, string> UpchargeSecondDayAirAM { get { return _UpchargeSecondDayAirAM; } }
        public Dictionary<string, string> UpchargeSaver { get { return _UpchargeSaver; } }

        public Dictionary<string, string> PerPackageChargeCWT { get { return _PerPackageChargeCWT; } }
        public Dictionary<string, string> PerShipmentChargeCWT { get { return _PerShipmentChargeCWT; } }
        public Dictionary<string, string> UpchargeNextDayAirCWT { get { return _UpchargeNextDayAirCWT; } }
        public Dictionary<string, string> UpchargeSecondDayAirCWT { get { return _UpchargeSecondDayAirCWT; } }
        public Dictionary<string, string> UpchargeGroundCWT { get { return _UpchargeGroundCWT; } }
        public Dictionary<string, string> UpchargeThreeDaySelectCWT { get { return _UpchargeThreeDaySelectCWT; } }
        public Dictionary<string, string> UpchargeNextDayAirSaverCWT { get { return _UpchargeNextDayAirSaverCWT; } }
        public Dictionary<string, string> UpchargeNextDayAirEarlyAMCWT { get { return _UpchargeNextDayAirEarlyAMCWT; } }
        public Dictionary<string, string> UpchargeSecondDayAirAMCWT { get { return _UpchargeSecondDayAirAMCWT; } }
        public Dictionary<string, string> UpchargeSaverCWT { get { return _UpchargeSaverCWT; } }

        // Default: if no parameters are given return the constructor override.
        public RateCalculations() : this(0, Carriers.UPS) { }

        /// <summary>
        /// Returns the rate table for each plant.
        /// </summary>
        /// <param name="accountNumber"></param>
        /// <param name="CWT"></param>
        public RateCalculations(int accountNumber, Carriers carrier)
        {
            SqlConnection sqlConnection = new SqlConnection(Configuration.UpsRateSqlConnection);
            sqlConnection.Open();

            SqlCommand sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "GetPlantCharges";
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = carrier.ToString();
            sqlCommand.Parameters.Add("AcctNumber", System.Data.SqlDbType.Int).Value = accountNumber;

            if (carrier == Carriers.UPSCWT) {
                using (SqlDataReader reader = sqlCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        this._PerPackageChargeCWT.Add(reader["PlantCode"].ToString(), reader["PerPackageCharge"].ToString());
                        this._PerShipmentChargeCWT.Add(reader["PlantCode"].ToString(), reader["PerShipmentCharge"].ToString());

                        this._UpchargeNextDayAirCWT.Add(reader["PlantCode"].ToString(), reader["NextDayAir"].ToString());
                        this._UpchargeSecondDayAirCWT.Add(reader["PlantCode"].ToString(), reader["SecondDayAir"].ToString());
                        this._UpchargeGroundCWT.Add(reader["PlantCode"].ToString(), reader["Ground"].ToString());
                        this._UpchargeThreeDaySelectCWT.Add(reader["PlantCode"].ToString(), reader["ThreeDaySelect"].ToString());
                        this._UpchargeNextDayAirSaverCWT.Add(reader["PlantCode"].ToString(), reader["NextDayairSaver"].ToString());
                        this._UpchargeNextDayAirEarlyAMCWT.Add(reader["PlantCode"].ToString(), reader["NextDayAirEarlyAM"].ToString());
                        this._UpchargeSecondDayAirAMCWT.Add(reader["PlantCode"].ToString(), reader["SecondDayAirAM"].ToString());
                        this._UpchargeSaverCWT.Add(reader["PlantCode"].ToString(), reader["Saver"].ToString());
                    }
                }
            }
            else
            {
                using (SqlDataReader reader = sqlCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        this._PerPackageCharge.Add(reader["PlantCode"].ToString(), reader["PerPackageCharge"].ToString());
                        this._PerShipmentCharge.Add(reader["PlantCode"].ToString(), reader["PerShipmentCharge"].ToString());

                        this._UpchargeNextDayAir.Add(reader["PlantCode"].ToString(), reader["NextDayAir"].ToString());
                        this._UpchargeSecondDayAir.Add(reader["PlantCode"].ToString(), reader["SecondDayAir"].ToString());
                        this._UpchargeGround.Add(reader["PlantCode"].ToString(), reader["Ground"].ToString());
                        this._UpchargeThreeDaySelect.Add(reader["PlantCode"].ToString(), reader["ThreeDaySelect"].ToString());
                        this._UpchargeNextDayAirSaver.Add(reader["PlantCode"].ToString(), reader["NextDayairSaver"].ToString());
                        this._UpchargeNextDayAirEarlyAM.Add(reader["PlantCode"].ToString(), reader["NextDayAirEarlyAM"].ToString());
                        this._UpchargeSecondDayAirAM.Add(reader["PlantCode"].ToString(), reader["SecondDayAirAM"].ToString());
                        this._UpchargeSaver.Add(reader["PlantCode"].ToString(), reader["Saver"].ToString());
                    }
                }
            }

            sqlConnection.Close();
        }
        

        /// <summary>
        /// Calulates the Centum Weight (CWT) eligibility for all UPS air services.
        /// </summary>
        /// <param name="numberOfPackages"></param>
        /// <param name="packageWeight"></param>
        /// <param name="lastPackageWeight"></param>
        /// <returns></returns>
        public bool HundredWeightAirEligable(UPSService.ServiceCode serviceCode, int numberOfPackages, string packageWeight, string lastPackageWeight)
        {
            switch (serviceCode)
            {
                case UPSService.ServiceCode.NextDayAirEarlyAM:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);                    
                case UPSService.ServiceCode.NextDayAirSaver:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);
                case UPSService.ServiceCode.UPS2ndDayAir:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);
                case UPSService.ServiceCode.SecondDayAirAM:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);                    
                case UPSService.ServiceCode.UPSNextDayAir:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);                    
                default:
                    return false;
            }
        }

        /// <summary>
        /// Calulates the Centum Weight (CWT) eligibility for all UPS ground services.
        /// </summary>
        /// <param name="numberOfPackages"></param>
        /// <param name="packageWeight"></param>
        /// <param name="lastPackageWeight"></param>
        /// <returns></returns>
        public bool HundredWeightGroundEligable(UPSService.ServiceCode serviceCode, int numberOfPackages, string packageWeight, string lastPackageWeight)
        {
            switch (serviceCode)
            {
                case UPSService.ServiceCode.UPSGround:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);
                    break;
                case UPSService.ServiceCode.UPS3DaySelect:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);
                    break;
                case UPSService.ServiceCode.UPSSaver:
                    return HundredWeightEligable(serviceCode, numberOfPackages, packageWeight, lastPackageWeight);
                    break;
                default:
                    return false;
            }
        }

        internal static string CalculateRate(string accountNumber, string plantId, string serviceName, string currentRate, string CWTRate, int numberOfPackages, string packageWeight, string lastPackage)
        {
            int _accountNumber = 0;
            double rate = Convert.ToDouble(currentRate);
            double cwtRate = Convert.ToDouble(CWTRate);
            double noPackages = Convert.ToDouble(numberOfPackages);
            double total = 0.0;
            double markup = 0.0;
            double perPackageCharge = 0.0;
            double perShipmentCharge = 0.0;
            double hundredWeightAdjustment = cwtRate / .7;
            bool cwt = false;
            double totalWeight = (Convert.ToInt16(packageWeight) * (noPackages - 1)) + Convert.ToInt16(lastPackage);

            RateCalculations rateCalculations = new RateCalculations();

            if (accountNumber.IsNullOrWhiteSpace())
            {
                _accountNumber = 0;
            }
            else
            {
                _accountNumber = int.Parse(accountNumber);
            }

            switch (serviceName)
            {
                case "UPSGround":
                    if (rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPSGround, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);                        
                        markup = Convert.ToDouble(rateCalculations.UpchargeGroundCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeGround[plantId]);
                    }

                    break;
                case "UPS3DaySelect":
                    if (rateCalculations.HundredWeightGroundEligable(UPSService.ServiceCode.UPS3DaySelect, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeThreeDaySelectCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations._UpchargeThreeDaySelect[plantId]);
                    }
                    break;
                case "UPSNextDayAir":
                    if (rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.UPSNextDayAir, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeNextDayAirCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeNextDayAir[plantId]);
                    }
                    break;
                case "UPS2ndDayAir":
                    if (rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.UPS2ndDayAir, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeSecondDayAirCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeSecondDayAir[plantId]);
                    }
                    break;
                case "SecondDayAirAM":
                    if (rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.SecondDayAirAM, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeSecondDayAirAMCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeSecondDayAirAM[plantId]);
                    }
                    break;
                case "NextDayAirSaver":
                    if (rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.NextDayAirSaver, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeNextDayAirSaverCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeNextDayAirSaver[plantId]);
                    }

                    break;
                case "NextDayAirEarlyAM":                    
                    if (rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.NextDayAirEarlyAM, numberOfPackages, packageWeight, lastPackage))
                    {
                        //cwt = false; // Since no negotiated rate is returned and published rate are retuened the same we are not checking for CWT.
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeNextDayAirSaverCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeNextDayAirSaver[plantId]);
                    }
                    break;
                case "UPSSaver":
                    if (rateCalculations.HundredWeightAirEligable(UPSService.ServiceCode.UPSSaver, numberOfPackages, packageWeight, lastPackage))
                    {
                        cwt = true;
                        rateCalculations = new RateCalculations(_accountNumber, Carriers.UPSCWT);
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageChargeCWT[plantId]);
                        perShipmentCharge = Convert.ToDouble(rateCalculations.PerShipmentChargeCWT[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeSaverCWT[plantId]);
                    }
                    else
                    {
                        perPackageCharge = Convert.ToDouble(rateCalculations.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(rateCalculations.UpchargeSaver[plantId]);
                    }

                    break;
                case "UPSGroundFreight":                    
                    RateCalculations rateGFCalculations = new RateCalculations(0, Carriers.GF);
                    perPackageCharge = Convert.ToDouble(rateGFCalculations.PerPackageCharge[plantId]);
                    perShipmentCharge = Convert.ToDouble(rateGFCalculations.PerShipmentCharge[plantId]);
                    markup = Convert.ToDouble(rateGFCalculations.UpchargeGround[plantId]);
                    break;
            }

            bool isAirCWT = (noPackages >= Configuration.MinCWTPackagesAir) && (totalWeight >= Configuration.MinCWTWeightAir);
            bool isGroundCWT = (noPackages >= Configuration.MinCWTPackagesGround) && (totalWeight >= Configuration.MinCWTWeightGround);
            double markupPercentage = 0;

            #region -- Define Service CWT Types dictionary (mapping codes to Ground, Air, or Neither) --
            Dictionary<string, string> dServiceTypes = new Dictionary<string, string>();
            dServiceTypes.Add("UPSGround", "GROUND");
            dServiceTypes.Add("UPS3DaySelect", "GROUND");
            dServiceTypes.Add("UPSSaver", "GROUND");
            dServiceTypes.Add("UPSNextDayAir", "AIR");
            dServiceTypes.Add("UPS2ndDayAir", "AIR");            
            dServiceTypes.Add("NextDayAirSaver", "AIR");
            dServiceTypes.Add("SecondDayAirAM", "AIR");
            dServiceTypes.Add("NextDayAirEarlyAM", "AIR-NN"); //means AIR - No Negotiated Rate - on this service, we ignore negotiated rate for CWT as it is not allowed
            #endregion

            string serviceCWTType = string.Empty;
            if (dServiceTypes.ContainsKey(serviceName))
            {
                serviceCWTType = dServiceTypes[serviceName];
            }
            
            if (cwt && serviceName == "UPSGround" || serviceName == "SecondDayAirAM")
            {
                total = hundredWeightAdjustment;
                total += perShipmentCharge;
                total += (perPackageCharge * noPackages);
                total += (markup / 100) * hundredWeightAdjustment;
            }
            // Hundred weight adjustment
            else if (cwt  && serviceName != "UPSGroundFreight" && serviceName != "NextDayAirEarlyAM")
            { 
                total = hundredWeightAdjustment;                
                total += perShipmentCharge;
                total += (perPackageCharge * noPackages);
            }
            else if (serviceName == "UPSGroundFreight")
            {
                // Ground Freight should always use CWT Negotiated rates.
                total = cwtRate;               
                total += perShipmentCharge;
                total += (perPackageCharge * noPackages);
                total += ((markup / 100) * cwtRate);
            }
            else if (serviceName == "NextDayAirEarlyAM")
            {
                total = cwtRate;
                total += perShipmentCharge;
                total += (perPackageCharge * noPackages);
            }
            else { 
                total = rate;
                total += ((markup / 100) * rate);
                total += (perPackageCharge * noPackages);
            }       
            

            return total.ToString("C");
        }

        /// <summary>
        /// Determines whether or not the shipment is CWT eligible for the service.
        /// </summary>
        /// <param name="serviceCode"></param>
        /// <param name="numberOfPackages"></param>
        /// <param name="packageWeight"></param>
        /// <param name="lastPackageWeight"></param>
        /// <returns></returns>
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
                case UPSService.ServiceCode.UPS2ndDayAir:
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