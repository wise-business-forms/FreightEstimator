using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace AuthenticationServer.Models.Services
{
    public class UPSRequest
    {
        private string _request = string.Empty;
        private string _response = string.Empty;
        private string _url = string.Empty;
        private UPSService[] _uPSServices = null;
        private Shipment _shipment = null;
        public enum RequestOption { Rate, Shop, Ratetimeintransit, Shoptimeintransit };

        public UPSRequest() { }
        /// <summary>
        /// Builds the request to be sent to the UPS API.
        /// </summary>
        /// <param name="shipment"></param>
        /// <param name="plant"></param>
        /// <param name="requestOption"></param>
        public UPSRequest(Shipment shipment, Plant plant, RequestOption requestOption)
        {
            _request = RateRequest(shipment, new Plant(plant.Id), requestOption);
            _url = Configuration.UPSShopRatesURL + requestOption.ToString();
            _shipment = shipment;
        }

        /// <summary>
        /// Returns the response from the UPS API.
        /// </summary>
        /// <returns></returns>
        public string Response()
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetToken());

                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_url);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + GetToken());

                    // Write data to request stream
                    using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                    {
                        streamWriter.Write(_request);
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


                    Log.LogRequest_Rate("paulm", _shipment.Address, _shipment.City, _shipment.State_selection, _shipment.Zip, _shipment.Country_selection, _request, _response, "");
                }
                catch (WebException ex)
                {
                    using (var errorResponse = (HttpWebResponse)ex.Response)
                    {
                        using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                        {
                            string error = reader.ReadToEnd();
                            _response = error;
                            //Log.LogRequest_Rate("paulm", _shipment.Address, _shipment.City, _shipment.State_selection, _shipment.Zip, _shipment.Country_selection, _request, _response, "");
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }

                return _response;
            }
        }

        private UPSService ParseUPSService(JToken service)
        {
            UPSService uPSService = new UPSService();
            //uPSService.ShipFrom = shipment.PlantId;
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
            uPSService.CWT = "TBD";

            return uPSService;
        }

        public UPSService[] UPSServices { get {
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
                } else
                {
                    List<UPSService> serviceSort = new List<UPSService>();
                    serviceSort.Add(ParseUPSService(services));
                    return serviceSort.ToArray();
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

            //sb.Append("\"ShipmentRatingOptions\": {");
            //    sb.Append("\"TPFCNegotiatedRatesIndicator\": \"Y\",");
            //    sb.Append("\"NegotiatedRatesIndicator\": \"Y\"");
            //sb.Append("},"); // ShipmentRatingOptions
            sb.Append("\"Service\":");
            sb.Append("{\"Code\": \"03\",");
            sb.Append("\"Description\": \"UPS Worldwide Economy DDU\"");
            sb.Append("},"); // Service


            sb.Append("\"NumOfPieces\": \"" + shipment.number_of_packages + "\",");
            sb.Append("\"Package\":");
            sb.Append("{\"PackagingType\":");
            sb.Append("{\"Code\": \"00\",");
            sb.Append("\"Description\": \"Packaging\"");
            sb.Append("},"); // PackagingType
            sb.Append("\"Dimensions\":");
            sb.Append("{\"UnitOfMeasurement\":");
            sb.Append("{\"Code\": \"IN\",");
            sb.Append("\"Description\": \"Inches\"");
            sb.Append("},");
            sb.Append("\"Length\": \"5\",");
            sb.Append("\"Width\": \"5\",");
            sb.Append("\"Height\": \"5\"");
            sb.Append("},");
            sb.Append("\"PackageWeight\":");
            sb.Append("{\"UnitOfMeasurement\":");
            sb.Append("{\"Code\": \"LBS\",");
            sb.Append("\"Description\": \"Ounces\"");
            sb.Append("},");
            sb.Append("\"Weight\": \"" + shipment.billing_weight + "\"");
            sb.Append("},");

            if(requestOption == RequestOption.Rate)
            {
                sb.Append("\"Commodity\":");
                sb.Append("{\"FreightClass\": \"55\"");
                //sb.Append("{\"FreightClass\": \"" + shipment.freight_class_selected + "\"");
                sb.Append("},");
            }
            //sb.Append("\"OversizeIndicator\": \"X\",");
            sb.Append("\"MinimumBillableWeightIndicator\": \"X\"");
            sb.Append("}"); // RateRequest.Shipment.Package
            sb.Append("}"); // RateRequest.Shipment
            sb.Append("}"); // RateRequest
            sb.Append("}"); // ROOT
            return sb.ToString();
        }
    }
}