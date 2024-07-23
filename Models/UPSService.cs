using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class UPSService
    {
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
        public string ShipFrom { get; set; }
        public string ServiceName { get; set; }
        public string Rate { get; set; }
        public string CWT { get; set; }
    }
}