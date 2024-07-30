using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace AuthenticationServer.Models
{
    public class Configuration
    {
        static string _UPSAccessKey = "CC83ED82D080DC80";
        static string _UPSUserName = "WiseWebSupport";
        static string _UPSPassword = "Wise_forms";//"Nextwave1";//"wise_forms";
        static string _UPSClientId = "SzWbVRiGAPPbC0NqV9GGXdE8kUE0EnexGrxsl94sj0HGTdAX";
        static string _UPSClientSecret = "zcnbBCf3qPGLleJv1aBqOH8SbAbFssLoE1vAAUGbrnXK2GJAQJUTAskarDv70Ddw";
        static string _UPSAuthorizationURL = "https://onlinetools.ups.com/security/v1/oauth/authorize"; // PRODUCTION
        static string _UPSGenerateTokenURL = "https://onlinetools.ups.com/security/v1/oauth/token"; // PRODUCTION
        //static string _UPSShopRatesURL = "https://onlinetools.ups.com/rating/v2403/Shop"; // PRODUCTION {version}/{requestoption}
        //static string _UPSAddressValidationURL = "https://onlinetools.ups.com/api/addressvalidation/v1/3"; //  PRODUCTION  {version}/{requestOption}

        static string _UPSShipFromName = "Wise Alpharetta";
        static string _UPSShipFromAddress = "1000 Union Center Drive";
        static string _UPSShipFromCity = "Alpharetta";
        static string _UPSShipFromState = "GA";
        static string _UPSShipFromZip = "30004";

        // UPS TEST ENDPOINTS
        //static string _UPSAuthorizationURL = "https://wwwcie.ups.com/security/v1/oauth/authorize"; // TEST
        //static string _UPSGenerateTokenURL = "https://wwwcie.ups.com/security/v1/oauth/token"; // TEST
        static string _UPSAddressValidationURL = "https://wwwcie.ups.com/api/addressvalidation/v1/3"; // TEST  {version}/{requestOption}
        static string _UPSShopRatesURL = "https://wwwcie.ups.com/api/rating/v2403/Shop"; // TEST {version}/{requestoption}

        static string _UpsRateSqlConnection = "Server=AZUREDB01\\AZUREDB01;Initial Catalog=UpsRate;uid=sa;pwd=95Montana!!!;";
        static string _WiseLinkSqlConnection = "Data Source=AZUREDB01\\AZUREDB01;Initial Catalog=WBFDOTCOM;uid=sa;pwd=95Montana!!!;";

        static Dictionary<string, string> _UPSRateCode = new Dictionary<string, string>()
        {
            {"01", "UPS Next Day Air"},
            {"02", "UPS 2nd Day Air"},
            {"03", "UPS Ground"},
            {"07", "UPS Worldwide Express"},
            {"08", "UPS Worldwide Expedited"},
            {"11", "UPS Standard"},
            {"12", "UPS 3 Day Select"},
            {"13", "Next Day Air Saver"},
            {"14", "Next Day Air Early AM"},
            {"54", "Express Plus"},
            {"59", "2nd Day Air AM"},
            {"65", "UPS Saver"},
            {"82", "UPS Today Standard"},
            {"83", "UPS Today Dedicated Courier"},
            {"84", "UPS Today Intercity"},
            {"85", "UPS Today Express"},
            {"86", "UPS Today Express Saver"}
        };

        static Dictionary<string, string> __plantLocations = new Dictionary<string, string>()
        {
            {"ALP", "Alpharetta"},
            {"BUT", "Butler"},
            {"FTW", "Fort Wayne"},
            {"PDT", "Piedmont"},
            {"POR", "Portland"},
            {"BMK", "Brandmark"},
            {"COR", "Corporate"},
            {"CIN", "Cincinnati"},
            {"EBT", "East Butler"}
        };

        static string _ShipFromShipperNumber = "391287";

        static string _NetworkDomain = "WISENT";

        static string[] _PlantCodesMultiRate = { "ALP", "BUT", "FTW", "POR", "BMK" };

        static double _MinCWTWeightGround = 200;
        static double _MinCWTPackagesGround = 2;

        static double _MinCWTWeightAir = 100;
        static double _MinCWTPackagesAir = 2;

        static Dictionary<string, string> _PlantNames = new Dictionary<string, string>();

        static string _M33DemoUrl = "http://demo.m33integrated.com/api/";
        static string _M33ProdUrl = "https://blackbeardapp.com/api/";

        static string _M33DemoToken = "696c42d819642885724b60ffcb7d636deadd632e";
        static string _M33ProdToken = "d579620ffba756e5c2ec9f76e3447f98bf85770b";

        static bool _M33DemoMode = false;

        static string _TransPlaceDemoUrl = "https://uattms.transplace.com/xml-api/api/";
        static string _TransPlaceProdUrl = "https://tms.transplace.com/xml-api/api/";

        static string _TransPlaceDemoToken = "8Hhk8etqxs94gEtj0JpVxl90qoINDfMAiemh84XbGNM%3D";
        static string _TransPlaceProdToken = "jLDnfupyBDGy%2FnF2zYs4gxMB78GTlIAytU2dzN4xQLdVWNGGmkNOl%2B9N%2BU6aO4vP";

        static bool _TransPlaceDemoMode = false;


        static Configuration()
        {
            populatePlantNames();
        }

        private static void populatePlantNames()
        {
            _PlantNames.Add("ALP", "Alpharetta");
            _PlantNames.Add("BUT", "Butler");
            _PlantNames.Add("FTW", "Ft Wayne");
            _PlantNames.Add("PDT", "Piedmont");
            _PlantNames.Add("POR", "Portland");
            _PlantNames.Add("BMK", "Brandmark");
            _PlantNames.Add("COR", "Corporate");
            _PlantNames.Add("CIN", "Cincinnati");
            _PlantNames.Add("EBT", "East Butler");

        }

        private static List<string> _UPSServiceOrder = new List<string>()
        {
            "UPSGround",
            "UPS3DaySelect",
            "UPS2ndDayAir",
            "SecondDayAirAM",
            "NextDayAirSaver",
            "UPSNextDayAir",
            "NextDayAirEarlyAM",


            "UPS2ndDayAir",                
            "UPSWorldwideExpress",
            "UPSWorldwideExpedited",
            "UPSStandard",  
            "ExpressPlus",            
            "UPSSaver",
            "UPSTodayStandard",
            "UPSTodayDedicatedCourier",
            "UPSTodayIntercity",
            "UPSTodayExpress",
            "UPSTodayExpressSaver"
        };

        private static List<SelectListItem> _delivery_signature_required_selection = new List<SelectListItem>()
        {
            new SelectListItem() { Text = "None", Value = "None" },
            new SelectListItem() { Text = "Delivery Confirmation", Value = "Delivery Confirmation" },
            new SelectListItem() { Text = "Del Conf / Signature Req'd", Value = "Delivery Confirmation / Signature Required" },
            new SelectListItem() { Text = "Del Conf / Adult Sig Req'd", Value = "Delivery Confirmation / Adult Signature Required" }
        };

        public static string UPSAccessKey
        {
            get { return _UPSAccessKey; }
        }
        public static string UPSUserName
        {
            get { return _UPSUserName; }
        }
        public static string UPSPassword
        {
            get { return _UPSPassword; }
        }
        public static string UPSClientId
        {
            get { return _UPSClientId; }
        }
        public static string UPSClientSecret
        {
            get { return _UPSClientSecret; }
        }
        public static string UPSAuthorizationURL
        {
            get { return _UPSAuthorizationURL; }
        }
        public static string UPSGenerateTokenURL
        {
            get { return _UPSGenerateTokenURL; }
        }
        public static string UPSAddressValidationURL
        {
            get { return _UPSAddressValidationURL; }
        }
        public static string UPSShopRatesURL
        {
            get { return _UPSShopRatesURL; }
        }
        public static string UpsRateSqlConnection
        {
            get { return _UpsRateSqlConnection; }
        }
        public static string WiseLinkSqlConnection
        {
            get { return _WiseLinkSqlConnection; }
        }
        public static string UPSShipFromName
        {
            get { return _UPSShipFromName; }
        }
        public static string UPSShipFromAddress
        {
            get { return _UPSShipFromAddress; }
        }
        public static string UPSShipFromCity
        {
            get { return _UPSShipFromCity; }
        }
        public static string UPSShipFromState
        {
            get { return _UPSShipFromState; }
        }
        public static string UPSShipFromZip
        {
            get { return _UPSShipFromZip; }
        }
        public static List<string> UPSServiceCodeOrder
        {
            get { return _UPSServiceOrder;  }
        }
        public static string ShipFromShipperNumber
        {
            get { return _ShipFromShipperNumber; }
        }
        public static string NetworkDomain
        {
            get { return _NetworkDomain; }
        }
        public static Dictionary<string, string> PlantLocations
        {
            get { return __plantLocations; }
        }
        public static List<SelectListItem> DeliverySignatureRequiredSelection
        {
            get { return _delivery_signature_required_selection; }
        }
        public static string[] PlantCodesMultiRate
        {
            get { return _PlantCodesMultiRate; }
        }
        public static double MinCWTWeightGround
        {
            get { return _MinCWTWeightGround; }
        }
        public static double MinCWTPackagesGround
        {
            get { return _MinCWTPackagesGround; }
        }
        public static double MinCWTWeightAir
        {
            get { return _MinCWTWeightAir; }
        }
        public static double MinCWTPackagesAir
        {
            get { return _MinCWTPackagesAir; }
        }
        public static Dictionary<string, string> PlantNames
        {
            get { return _PlantNames; }
        }
        public static string M33Url
        {
            get
            {
                if (_M33DemoMode)
                {
                    return _M33DemoUrl;
                }
                else
                {
                    return _M33ProdUrl;
                }
            }
        }
        public static string M33Token
        {
            get
            {
                if (_M33DemoMode)
                {
                    return _M33DemoToken;
                }
                else
                {
                    return _M33ProdToken;
                }
            }
        }


        public static string TransPlaceUrl
        {
            get
            {
                if (_TransPlaceDemoMode)
                {
                    return _TransPlaceDemoUrl;
                }
                else
                {
                    return _TransPlaceProdUrl;
                }
            }
        }
        public static string TransPlaceToken
        {
            get
            {
                if (_TransPlaceDemoMode)
                {
                    return _TransPlaceDemoToken;
                }
                else
                {
                    return _TransPlaceProdToken;
                }
            }
        }
    }
}