using AuthenticationServer.Models.Carrier.UPS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class UPSService
    {
        private Dictionary<string, string> _CWTCodes;

        public string CWT { get; set; }
        public string CWT_Adjustment { get; set; }
        public string Direct {  get; set; }
        public string PlantCode { get; set; }
        public double CustomerRate { get; set; }
        public enum ServiceCode
        {
            UPSNextDayAir = 01,
            UPS2ndDayAir = 02,
            UPSGround = 03,
            UPSWorldwideExpress = 07,
            UPSWorldwideExpedited = 08,
            UPSStandard = 11,
            UPS3DaySelect = 12,
            NextDayAirSaver = 13,
            NextDayAirEarlyAM = 14,
            ExpressPlus = 54,
            SecondDayAirAM = 59,
            UPSSaver = 65,
            UPSTodayStandard = 82,
            UPSTodayDedicatedCourier = 83,
            UPSTodayIntercity = 84,
            UPSTodayExpress = 85,
            UPSTodayExpressSaver = 86
        }
        public Dictionary<string, string> CWTCodes
        {
            get
            {
                if (_CWTCodes == null)
                {
                    _CWTCodes = new Dictionary<string, string>
                    {
                        { "01","AIR" },
                        { "02","AIR" },
                        { "03","GROUND" },
                        { "12","GROUND" },
                        { "13","AIR" },
                        { "14","AIR-NN" },
                        { "59","AIR" },
                        { "65","GROUND" },
                    };
                }
                return _CWTCodes;
            }
        }
        public string ServiceName { get; set; }
        public string ShipFrom { get; set; }   
        public string TotalCost { get; set; }
        public string TransitDays { get; set; }        
        
        public string Plant_CarrierId { get; set; }
        public string Plant_PerPackageCharge { get; set; }
        public string Plant_ShipmentCharge { get; set; }
        /// <summary>
        /// This is the service charge for this rate.
        /// </summary>
        public string Plant_Surcharge { get; set; }

        /// <summary>
        /// The original value for the accessorial costs associated with the shipment.        
        /// </summary>
        public string RatedShipment_AccessorialCharges_MonetaryValue { get; set; }

        /// <summary>
        /// The original value for the transportation costs associated with the shipment.        
        /// </summary>
        public string RatedShipment_TransportationCharges_MonetaryValue { get; set; }
        /// <summary>
        /// The original base value of the specific service for the shipment.
        /// </summary>
        public string RatedShipment_BaseServiceCharge_MonetaryValue { get; set; }
        /// <summary>
        /// The original value for the accessorial charges associated with the shipment.
        /// </summary>
        public string RatedShipment_ServiceOptionsCharges_MonetaryValue { get; set; }
        /// <summary>
        /// The value for the total charges associated with the shipment.
        /// </summary>
        public string RatedShipment_TotalCharges_MonetaryValue { get; set; }
        /// <summary>
        /// The value for the Negotiated Rate total charges associated with the shipment.
        /// </summary>
        public string RatedShipment_NegotiatedRateCharges_TotalCharge { get; set; }
    }

    public class UPSSort : IComparer<UPSService>
    {
        public int Compare(UPSService x, UPSService y)
        {
            int indexX = Configuration.UPSServiceCodeOrder.IndexOf(x.ServiceName);
            int indexY = Configuration.UPSServiceCodeOrder.IndexOf(y.ServiceName);

            /*
             *Only for detecting if a value is not in original list.
            if(indexX == -1 || indexY == -1)
            {
                throw new ArgumentException("String not found in custom order list.  Check the configfile.");
            }
            */
            return indexX.CompareTo(indexY);
        }
    }
}