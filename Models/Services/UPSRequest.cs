﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using System.Web.Http.ExceptionHandling;
using System.Data.Entity.Core.Mapping;
using Microsoft.Ajax.Utilities;

namespace AuthenticationServer.Models.Services
{
    public class UPSRequest { 

        private string _response = String.Empty;
        private Shipment _shipment = null;
        private UPSService[] _uPSServices = null;

        public enum RequestOption { Rate, Shop, Ratetimeintransit, Shoptimeintransit };
        int _counter = 0;

        public UPSRequest() { }
        /// <summary>
        /// Builds the request to be sent to the UPS API.
        /// </summary>
        /// <param name="shipment"></param>
        /// <param name="plant"></param>
        /// <param name="requestOption"></param>
        public UPSRequest(Shipment shipment, Plant plant, RequestOption requestOption)
        {
            _shipment = shipment;

            // For rating without address.
            if (_shipment.Address == null || _shipment.Address.Trim() == "") _shipment.Address = "";
            if (_shipment.City == null || _shipment.City.Trim() == "") _shipment.City = "";
            if (_shipment.State_selection == null || _shipment.State_selection.Trim() == "") _shipment.State_selection = "";
            if (_shipment.number_of_packages == 0)
            {
                _shipment.number_of_packages = 1;
                _shipment.package_weight = 1;
            }

            // Address Vlidation
            string _request = AddressValidationRequest(shipment, plant, requestOption);
            string _url = Configuration.UPSAddressValidationURL;
            string _response = Response(_request, _url);

            JObject classification = JObject.Parse(_response);
            shipment.Address_Classification = (string)classification["XAVResponse"]?["Candidate"]?["AddressClassification"]?["Description"];


            // Rate Request
            _request = RateRequest(shipment, new Plant(plant.Id), requestOption);
            _url = Configuration.UPSShopRatesURL + requestOption.ToString();            
            _response = Response(_request, _url);
        }
       

        /// <summary>
        /// Returns the response from the UPS API.
        /// </summary>
        /// <returns></returns>
        private string Response(string rateRequest, string url)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetToken());

                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + GetToken());
                   


                    // Write data to request stream
                    using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                    {
                        streamWriter.Write(rateRequest);
                    }

                    // Get the response
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var streamReader = new StreamReader(response.GetResponseStream()))
                            {
                                _response = streamReader.ReadToEnd();  
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Error: {response.StatusCode} - {response.StatusDescription}");
                        }
                    }


                    Log.LogRequest_Rate("", _shipment.Address, _shipment.City, _shipment.State_selection, _shipment.Zip, _shipment.Country_selection, rateRequest, _response, "");
                }
                catch (WebException ex)
                {
                    _response = ex.Message.ToString();
                    Log.LogRequest_Rate("", _shipment.Address, _shipment.City, _shipment.State_selection, _shipment.Zip, _shipment.Country_selection, rateRequest, ex.Message, "");
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return _response;
        }

        private UPSService ParseUPSService(JToken service)
        {
            UPSService uPSService = new UPSService();
            var serviceCode = service.SelectToken("Service")?.SelectToken("Code")?.ToString() ?? "No Service Code";
            switch (serviceCode)
            {
                case "01":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSNextDayAir.ToString();
                    break;
                case "02":
                    uPSService.ServiceName = UPSService.ServiceCode.UPS2ndDayAir.ToString();
                    break;
                case "03":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSGround.ToString();
                    break;
                case "07":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSWorldwideExpress.ToString();
                    break;
                case "08":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSWorldwideExpedited.ToString();
                    break;
                case "11":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSStandard.ToString();
                    break;
                case "12":
                    uPSService.ServiceName = UPSService.ServiceCode.UPS3DaySelect.ToString();
                    break;
                case "13":
                    uPSService.ServiceName = UPSService.ServiceCode.NextDayAirSaver.ToString();
                    break;
                case "14":
                    uPSService.ServiceName = UPSService.ServiceCode.NextDayAirEarlyAM.ToString();
                    break;
                case "54":
                    uPSService.ServiceName = UPSService.ServiceCode.ExpressPlus.ToString();
                    break;
                case "59":
                    uPSService.ServiceName = UPSService.ServiceCode.SecondDayAirAM.ToString();
                    break;
                case "65":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSSaver.ToString();
                    break;
                case "82":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSTodayStandard.ToString();
                    break;
                case "83":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSTodayDedicatedCourier.ToString();
                    break;
                case "84":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSTodayIntercity.ToString();
                    break;
                case "85":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSTodayExpress.ToString();
                    break;
                case "86":
                    uPSService.ServiceName = UPSService.ServiceCode.UPSTodayExpressSaver.ToString();
                    break;
                default:
                    uPSService.ServiceName = "No Service Name";
                    break;
            }
            uPSService.PlantCode = _shipment.PlantId;
            uPSService.Rate = service.SelectToken("TotalCharges.MonetaryValue")?.ToString() ?? "-";
            uPSService.CWTRate = service.SelectToken("NegotiatedRateCharges.TotalCharge.MonetaryValue")?.ToString() ?? "-";
            uPSService.CWT = "TBD";

            return uPSService;
        }

        public UPSService[] UPSServices { get {
                try
                {
                    dynamic data = JObject.Parse(_response);

                    JToken services = data.SelectToken("RateResponse.RatedShipment");
                    if (services.Type == JTokenType.Array)
                    {
                        // Check for each key's existence and assign values accordingly
                        var serviceIndex = 0;
                        List<UPSService> serviceSort = new List<UPSService>();
                        foreach (var service in services)
                        {
                            serviceSort.Add(ParseUPSService(service));
                            serviceIndex++;
                        }

                        // Remove extra (null) UPS Services values slots from the array.
                        serviceSort.RemoveAll(service => service == null);

                        // Not the best place to sort for presentation ... but here we are.
                        serviceSort.Sort(new UPSSort());
                        _uPSServices = serviceSort.ToArray();
                    }
                    else
                    {
                        List<UPSService> serviceSort = new List<UPSService>();
                        serviceSort.Add(ParseUPSService(services));
                        return serviceSort.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Log.LogRequest_Rate("", _shipment.Address, _shipment.City, _shipment.State_selection, _shipment.Zip, _shipment.Country_selection, _response, ex.Message, "");
                }
                return _uPSServices;
            }
        } 

        public string XAVRequest { get; set; }
        public AddressKeyFormat AddressKeyFormat { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public string GetToken()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            string req = "grant_type=client_credentials";
            byte[] data = Encoding.ASCII.GetBytes(req);
            var accessID = Configuration.UPSClientId + ":" + Configuration.UPSClientSecret;
            var base64 = Convert.ToBase64String(Encoding.Default.GetBytes(accessID));
            string sResponse = "";
            string sToken = "";
            string url = Configuration.UPSGenerateTokenURL;

            WebRequest oRequest = WebRequest.Create(url);
            oRequest.Method = "POST";
            oRequest.ContentType = "application/x-www-form-urlencoded";
            oRequest.Headers.Add("Authorization", "Basic " + base64);
            oRequest.Headers.Add("x-merchant-id", Configuration.ShipFromShipperNumber);

            try
            {
                oRequest.GetRequestStream().Write(data, 0, data.Length);
                HttpWebResponse oResponse = (HttpWebResponse)oRequest.GetResponse();
                sResponse = new StreamReader(oResponse.GetResponseStream()).ReadToEnd();
                using (JsonDocument doc = JsonDocument.Parse(sResponse))
                {
                    JsonElement root = doc.RootElement;
                    sToken = root.GetProperty("access_token").GetString();
                }
            }
            catch (Exception ex)
            {
                sToken = ex.Message;
            }

            return sToken;
        }

        private string AddressValidationRequest(Shipment shipment, Plant plant, RequestOption requestOption)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"XAVRequest\": {");
            sb.Append("\"AddressKeyFormat\": {");
            sb.Append("\"ConsigneeName\": \"").Append(string.Empty).Append("\",");
            sb.Append("\"BuildingName\": \"").Append(string.Empty).Append("\",");
            sb.Append("\"AddressLine\": [\"").Append(shipment.Address).Append("\",").Append("\"\",").Append("\"\"],");
            sb.Append("\"Region\": \"").Append(shipment.City).Append(",").Append(shipment.State_selection).Append(",").Append(shipment.Zip).Append("\",");
            sb.Append("\"PoliticalDivision2\": \"").Append(string.Empty).Append("\",");
            sb.Append("\"PoliticalDivision1\": \"").Append(shipment.State_selection).Append("\",");
            sb.Append("\"PostcodePrimaryLow\": \"").Append(shipment.Zip).Append("\",");
            sb.Append("\"PostcodeExtendedLow\": \"").Append(string.Empty).Append("\",");
            sb.Append("\"Urbanization\": \"").Append(string.Empty).Append("\",");
            sb.Append("\"CountryCode\": \"").Append(shipment.Country_selection).Append("\"");
            sb.Append("}"); // AddressKeyFormat
            sb.Append("}"); // XAVRequest
            sb.Append("}"); // Root

            return sb.ToString();
        }

        /// <summary>
        /// Builds the JSON shipment request for the UPS Rate API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private string RateRequest(Shipment shipment, Plant plant, RequestOption requestOption)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"RateRequest\":");

            sb.Append("{\"Request\":");
            sb.Append("{\"TransactionReference\":{\"CustomerContext\": \"CustomerContext\"}"); // TransactionReference
            sb.Append("},"); // Request

            sb.Append("\"Shipment\":");
            sb.Append("{\"Shipper\":");
            sb.Append("{\"Name\": \"" + plant.Name + "\",");
            sb.Append("\"ShipperNumber\": \"" + Configuration.ShipFromShipperNumber + "\",");
            sb.Append("\"Address\":");
            sb.Append("{\"AddressLine\": [");
            sb.Append("\"" + plant.Address + "\",");
            sb.Append("\"\",");
            sb.Append("\"\"");
            sb.Append("],"); // AddressLine
            sb.Append("\"City\": \"" + plant.City + "\",");
            sb.Append("\"StateProvinceCode\": \"" + plant.State + "\",");
            sb.Append("\"PostalCode\": \"" + plant.Zip + "\",");
            sb.Append("\"CountryCode\": \"" + plant.Country + "\"");
            sb.Append("}"); // Address
            sb.Append("},"); // Shipper
            
            sb.Append("\"ShipTo\":");
            sb.Append("{\"Name\": \"" + shipment.AcctNum + "\",");
            sb.Append("\"Address\":");
            sb.Append("{\"AddressLine\": [");
            sb.Append("\"" + shipment.Address + "\",");
            sb.Append("\"\",");
            sb.Append("\"\"");
            sb.Append("],");
            sb.Append("\"City\": \"" + shipment.City + "\",");
            sb.Append("\"StateProvinceCode\": \"" + shipment.State_selection + "\",");
            sb.Append("\"PostalCode\": \"" + shipment.Zip + "\",");
            sb.Append("\"CountryCode\": \"" + shipment.Country_selection + "\"");
            sb.Append("}"); // Address
            sb.Append("},"); // ShipTo

            sb.Append("\"ShipFrom\":");
            sb.Append("{\"Name\": \"" + plant.Name + "\",");
            sb.Append("\"Address\":");
            sb.Append("{\"AddressLine\": [");
            sb.Append("\"" + plant.Address + "\",");
            sb.Append("\"\",");
            sb.Append("\"\"");
            sb.Append("],");
            sb.Append("\"City\": \"" + plant.City + "\",");
            sb.Append("\"StateProvinceCode\": \"" + plant.State + "\",");
            sb.Append("\"PostalCode\": \"" + plant.Zip + "\",");
            sb.Append("\"CountryCode\": \"" + plant.Country + "\"");
            sb.Append("}"); // Address
            sb.Append("},"); // ShipFrom

            // Ground Freight
            if(requestOption == RequestOption.Rate) {
                sb.Append("\"FRSPaymentInformation\": {\"Type\": {\"Code\": \"01\"}},");
            } else
            {
                sb.Append("\"PaymentDetails\":");
                sb.Append("{\"ShipmentCharge\":");
                sb.Append("{\"Type\": \"01\",");
                sb.Append("\"BillShipper\": {\"AccountNumber\": \"" + Configuration.ShipFromShipperNumber + "\"}");
                sb.Append("}"); // ShipmentCharge
                sb.Append("},"); // PaymentDetails
            }

            sb.Append("\"ShipmentRatingOptions\": {");
            //sb.Append("\"TPFCNegotiatedRatesIndicator\": \"Y\",");
            
            // Ground Freight
            if (requestOption == RequestOption.Rate)
            {               
               sb.Append("\"FRSShipmentIndicator\": \"\",");
            }

            sb.Append("\"NegotiatedRatesIndicator\": \"\"");
            sb.Append("},"); // ShipmentRatingOptions


            sb.Append("\"Service\":");
            sb.Append("{\"Code\": \"03\",");
            sb.Append("\"Description\": \"UPS Worldwide Economy DDU\"");
            sb.Append("},"); // Service


            sb.Append("\"NumOfPieces\": \"" + shipment.number_of_packages + "\",");
            
            // Add the packages to the request but be mindful of the last package if it is different.
            sb.Append("\"Package\":");            
            sb.Append("[");

            // Add the packages...
            if (shipment.package_weight != shipment.last_package_weight)
            {
                for (int p = 1; p <= shipment.number_of_packages - 1; p++)
                {
                    sb.Append(Package(shipment.package_weight, requestOption, shipment.freight_class_selected.ToString()));
                    sb.Append(", ");
                }

                // Packages have to weight something.  Sometimes people will not give the last package a weight if it is the same.
                if (shipment.last_package_weight == 0 || shipment.last_package_weight.ToString().Trim().IsNullOrWhiteSpace()) shipment.last_package_weight = shipment.package_weight;

                // Add last package
                sb.Append(Package(shipment.last_package_weight, requestOption, shipment.freight_class_selected.ToString()));
            }
            else  // all packages are the same weight.
            {
                for (int p = 1; p <= shipment.number_of_packages; p++)
                {
                    sb.Append(Package(shipment.package_weight, requestOption, shipment.freight_class_selected.ToString()));
                    if (p < shipment.number_of_packages) sb.Append(", ");
                }
            }
            sb.Append("],");
            

            if(requestOption == RequestOption.Rate)
            {
                sb.Append("\"Commodity\":");
                sb.Append("{\"FreightClass\": \"55\"");
                //sb.Append("{\"FreightClass\": \"" + shipment.freight_class_selected + "\"");
                sb.Append("},");
            }
            //sb.Append("\"OversizeIndicator\": \"X\",");
            sb.Append("\"MinimumBillableWeightIndicator\": \"X\"");
            sb.Append("}"); // RateRequest.Shipment
            sb.Append("}"); // RateRequest
            sb.Append("}"); // ROOT
            return sb.ToString();
        }

        private string Package(float package_weight, RequestOption requestOption, string freightClass)
        {
            _counter++;
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"PackagingType\":");

                sb.Append("{\"Code\": \"02\",");
                sb.Append("\"Description\": \"Packaging\"");
                sb.Append("},"); // Code

            sb.Append("\"Dimensions\":");
                sb.Append("{\"UnitOfMeasurement\":");
                    sb.Append("{\"Code\": \"IN\",");
                        sb.Append("\"Description\": \"Inches\"");
                    sb.Append("},");
                    sb.Append("\"Length\": \"5\",");
                    sb.Append("\"Width\": \"5\",");
                    sb.Append("\"Height\": \"5\"");
                sb.Append("},"); // Dimentions

            sb.Append("\"PackageWeight\":");
                sb.Append("{\"UnitOfMeasurement\":");
                    sb.Append("{\"Code\": \"LBS\",");
                    sb.Append("\"Description\": \"Pounds\"");
                    sb.Append("},");  // UnitOfMeasurement
            sb.Append("\"Weight\": \"" + package_weight + "\"");            
            sb.Append("}"); // PackageWeight

            // Ground Freight
            if (requestOption == RequestOption.Rate)
            {
                //sb.Append(",\"Commodity\": {\"FreightClass\": \"" + freightClass + "\"}");
                sb.Append(",\"Commodity\": {\"FreightClass\": \"55\"}");
            }

            sb.Append("}"); // PackagingType
            return sb.ToString();
        }
    }
}