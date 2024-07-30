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
        public string Direct {  get; set; }
        public string PlantCode { get; set; }
        public string Rate { get; set; }
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
    }

    public class UPSSort : IComparer<UPSService>
    {
        public int Compare(UPSService x, UPSService y)
        {
            int indexX = Configuration.UPSServiceCodeOrder.IndexOf(x.ServiceName);
            int indexY = Configuration.UPSServiceCodeOrder.IndexOf(y.ServiceName);

            if(indexX == -1 || indexY == -1)
            {
                throw new ArgumentException("String not found in custom order list.  Check the configfile.");
            }

            return indexX.CompareTo(indexY);
        }
    }
}