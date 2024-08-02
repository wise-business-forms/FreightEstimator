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

        public RateCalculations() { }


        /// <summary>
        /// Returns the rate table for each plant.
        /// </summary>
        /// <param name="accountNumber"></param>
        /// <param name="CWT"></param>
        public RateCalculations(int accountNumber, bool CWT)
        {
            string _carrier = "UPSCWT";

            if (!CWT)
            {
                _carrier = "UPS";
            }

            SqlConnection sqlConnection = new SqlConnection(Configuration.UpsRateSqlConnection);
            sqlConnection.Open();

            SqlCommand sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "GetPlantCharges";
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = _carrier;
            sqlCommand.Parameters.Add("AcctNumber", System.Data.SqlDbType.Int).Value = accountNumber;

            if (CWT) {
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
        public Dictionary<string, string> UpchargeSecondDayAirCWT { get { return _UpchargeSecondDayAirCWT;  } }
        public Dictionary<string, string> UpchargeGroundCWT { get { return _UpchargeGroundCWT;  } }
        public Dictionary<string, string> UpchargeThreeDaySelectCWT { get { return _UpchargeThreeDaySelectCWT; } }
        public Dictionary<string, string> UpchargeNextDayAirSaverCWT { get { return _UpchargeNextDayAirSaverCWT;  } }
        public Dictionary<string, string> UpchargeNextDayAirEarlyAMCWT { get { return _UpchargeNextDayAirEarlyAMCWT;  } }
        public Dictionary<string, string> UpchargeSecondDayAirAMCWT { get { return _UpchargeSecondDayAirAMCWT; } }
        public Dictionary<string, string> UpchargeSaverCWT { get { return _UpchargeSaverCWT; } }

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

        internal static string CalculateRate(string accountNumber, string plantId, string serviceName, string currentRate, int numberOfPackages, string packageWeight, string lastPackage)
        {
            double rate = Convert.ToDouble(currentRate);
            double noPackages = Convert.ToDouble(numberOfPackages);
            double total = 0.0;
            double markup = 0.0;
            double perPackageCharge = 0.0;

            RateCalculations rateCalculations = new RateCalculations();

            switch (serviceName)
            {
                case "UPSGround":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeGroundCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeGround[plantId]);
                    }

                    break;
                case "UPS3DaySelect":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeThreeDaySelectCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate._UpchargeThreeDaySelect[plantId]);
                    }
                    break;
                case "UPSNextDayAir":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeNextDayAirCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeNextDayAir[plantId]);
                    }
                    break;
                case "UPS2ndDayAir":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeSecondDayAirCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeSecondDayAir[plantId]);
                    }
                    break;
                case "SecondDayAirAM":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeSecondDayAirAMCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeSecondDayAirAM[plantId]);
                    }
                    break;

                case "NextDayAirSaver":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeNextDayAirSaverCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeNextDayAirSaver[plantId]);
                    }

                    break;

                case "NextDayAirEarlyAM":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeNextDayAirEarlyAMCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeNextDayAirEarlyAM[plantId]);
                    }
                    break;

                case "UPSSaver":
                    if (rateCalculations.HundredWeightGroundEligable(numberOfPackages, packageWeight, lastPackage))
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), true);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageChargeCWT[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeSaverCWT[plantId]);
                    }
                    else
                    {
                        RateCalculations groundRate = new RateCalculations(int.Parse(accountNumber), false);
                        perPackageCharge = Convert.ToDouble(groundRate.PerPackageCharge[plantId]);
                        markup = Convert.ToDouble(groundRate.UpchargeSaver[plantId]);
                    }

                    break;


            }
            total = rate;
            total += ((markup / 100) * rate);
            total = (perPackageCharge * noPackages) + rate;
            return total.ToString();
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