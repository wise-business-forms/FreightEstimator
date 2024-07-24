using AuthenticationServer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web.Mvc;
using System.Linq;
using AuthenticationServer.Models.Service;

namespace AuthenticationServer.Controllers
{
    public class PlantController : Controller
    {
        // GET: Plant
        public ActionResult Index(string loc)
        {
            ViewBag.Param1 = loc;
            if(string.IsNullOrEmpty(loc) || !Configuration.PlantLocations.ContainsKey(loc.ToUpper()))
            {
                return View("Error");
            }
            var plantName = Configuration.PlantLocations[loc.ToUpper()];            
            var model = new Models.Shipment { };

            // Setup default values for the shipment
            model.PlantId = loc.ToUpper();
            model.PlantName = plantName;

            model.delivery_signature_required = Configuration.DeliverySignatureRequiredSelection;
            model.multiple_location_rate = new List<SelectListItem> { new SelectListItem { Text = "No", Value = "No" }, new SelectListItem { Text = "Yes", Value = "Yes" } };
            model.include_ground_rate = new List<SelectListItem> { new SelectListItem { Text = "Yes", Value = "Yes" }, new SelectListItem { Text = "No", Value = "No" } };
            model.include_ltl_rate = new List<SelectListItem> { new SelectListItem { Text = "Yes", Value = "Yes" }, new SelectListItem { Text = "No", Value = "No" } };

            model.delivery_signature_required_selction = "No";
            model.multiple_location_rate_selection = "No";
            model.include_ground_rate_selection = "No";
            model.include_ltl_rate_selection = "No";

            return View(model);
        }

        [HttpPost]
        public ActionResult SubmitShipment(Shipment shipment)
        {
            if (ModelState.IsValid)
            {
                // Save the shipment to the database
                return RedirectToAction("ShipmentConfirmation", shipment);
            }
            return View("Shipment", shipment);
        }

        public ActionResult ShipmentConfirmation(Shipment shipment)
        {
            //ValidateStateForCWT(shipment);            

            shipment.billing_weight = shipment.number_of_packages * shipment.package_weight;

            shipment.shopRateResponse = ShopRateResponse(shipment);

            return View(shipment);
        }

        private void ValidateStateForCWT(Shipment shipment)
        {
            var CWTAirMinPkgs = Configuration.MinCWTPackagesAir;
            var CWTAirMinWeight = Configuration.MinCWTWeightAir;
            var CWTGroundMinPkgs = Configuration.MinCWTPackagesGround;
            var CWTGroundMinWeight = Configuration.MinCWTWeightGround;

            var StateIsValid = false;
            var IsHundredWeight = false;
            var NumberofPackages = shipment.number_of_packages;
            var PackageWeight = shipment.package_weight;
            var LastPackageWeight = shipment.last_package_weight;

            float TotalWeight = 0;

            if ((NumberofPackages != 0) && (PackageWeight != 0))
            {
                try
                {                    
                    if (LastPackageWeight != 0)
                    {
                        TotalWeight = (NumberofPackages - 1) * PackageWeight + LastPackageWeight;
                    }
                    else
                    {
                        TotalWeight = NumberofPackages * PackageWeight;
                    }

                    if(((NumberofPackages >= CWTAirMinPkgs) && (TotalWeight >= CWTAirMinWeight)) || ((NumberofPackages >= CWTGroundMinPkgs) && (TotalWeight >= CWTGroundMinWeight)))
                    {
                        IsHundredWeight = true;
                    }
                    else
                    {
                        IsHundredWeight = false;
                    }
                } catch (Exception ex)
                {
                    throw new Exception("Error calculating total weight: " + ex.Message);
                }
            }
            else
            {
                IsHundredWeight = false;
            }

            if (IsHundredWeight) {
                StateIsValid = (shipment.State.Length >= 2);
            }
            else
            {
                StateIsValid = true;
            }
        }

        private string GetToken()
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
                using(JsonDocument doc = JsonDocument.Parse(sResponse))
                {
                    JsonElement root = doc.RootElement;
                    sToken = root.GetProperty("access_token").GetString();
                }
            }catch (Exception ex)
            {
                sToken = ex.Message;
            }

            return sToken;
        }

        private AddressKeyFormat AddressValidation(Shipment shipment)
        {
            string addressValidationRequest = AddressValidationRequest(shipment);
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetToken());

                try
                {
                    // Create HttpWebRequest
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Configuration.UPSAddressValidationURL);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + GetToken());

                    // Write data to request stream
                    using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                    {
                        streamWriter.Write(addressValidationRequest);
                    }

                    // Get the response
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var streamReader = new StreamReader(response.GetResponseStream()))
                            {
                                string result = streamReader.ReadToEnd();
                                UPSResponse uPSResponse = new UPSResponse();
                                AddressKeyFormat addressKeyFormat = new AddressKeyFormat();

                                dynamic data = JObject.Parse(result);

                                // Check for each key's existence and assign values accordingly
                                var candidateToken = data.SelectToken("XAVResponse.Candidate.AddressKeyFormat");
                                if (candidateToken != null)
                                {
                                    addressKeyFormat.AddressLine = candidateToken.SelectToken("AddressLine")?.ToString() ?? "No Address Line";
                                    addressKeyFormat.PoliticalDivision2 = candidateToken.SelectToken("PoliticalDivision2")?.ToString() ?? "No City";
                                    addressKeyFormat.PoliticalDivision1 = candidateToken.SelectToken("PoliticalDivision1")?.ToString() ?? "No State";
                                    addressKeyFormat.PostcodePrimaryLow = candidateToken.SelectToken("PostcodePrimaryLow")?.ToString() ?? "No Primary Zip Code";
                                    addressKeyFormat.PostcodeExtendedLow = candidateToken.SelectToken("PostcodeExtendedLow")?.ToString() ?? "No Extended Zip Code";
                                    addressKeyFormat.Region = candidateToken.SelectToken("Region")?.ToString() ?? "No Region";
                                    addressKeyFormat.CountryCode = candidateToken.SelectToken("CountryCode")?.ToString() ?? "No Country Code";
                                }
                                else
                                {
                                    Console.WriteLine("Candidate or AddressKeyFormat not found in the response.");
                                }

                                return addressKeyFormat;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Error: {response.StatusCode} - {response.StatusDescription}");
                        }
                    }
                }
                catch (WebException ex)
                {
                    if (ex.Response != null)
                    {
                        using (var errorResponse = (HttpWebResponse)ex.Response)
                        {
                            using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                            {
                                string error = reader.ReadToEnd();
                                Console.WriteLine("Error response JSON:");
                                Console.WriteLine(error);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("WebException: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: " + ex.Message);
                }

                return null;

            }
        }

        private ShopRateResponse ShopRateResponse(Shipment shipment)
        {
            ShopRateResponse shopRateResponse = new ShopRateResponse();
            shopRateResponse.UPSServices = new UPSService[10]; // More than needed. 10 is arbitrary

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetToken());

                try
                {
                    // Create HttpWebRequest
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Configuration.UPSShopRatesURL);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Headers.Add("Authorization", "Bearer " + GetToken());

                    // Write data to request stream
                    using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                    {
                        var rr = RateRequest(shipment);
                        streamWriter.Write(rr);
                    }

                    // Get the response
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var streamReader = new StreamReader(response.GetResponseStream()))
                            {
                                string result = streamReader.ReadToEnd();

                                dynamic data = JObject.Parse(result);

                                // Check for each key's existence and assign values accordingly
                                IList<JToken> services = data.SelectToken("RateResponse.RatedShipment");
                                var serviceIndex = 0;
                                foreach (var service in services)
                                {
                                    UPSService uPSService = new UPSService();
                                    uPSService.ShipFrom = shipment.PlantId;
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
                                    uPSService.Rate = service.SelectToken("TotalCharges.MonetaryValue")?.ToString() ?? "-";
                                    uPSService.CWT = "TBD";
                                    shopRateResponse.UPSServices[serviceIndex] = uPSService;
                                    serviceIndex++;
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Error: {response.StatusCode} - {response.StatusDescription}");
                        }
                    }
                 }
                catch (WebException ex)
                {
                    if (ex.Response != null)
                    {
                        using (var errorResponse = (HttpWebResponse)ex.Response)
                        {
                            using (var reader = new StreamReader(errorResponse.GetResponseStream()))
                            {
                                string error = reader.ReadToEnd();
                                Console.WriteLine("Error response JSON:");
                                Console.WriteLine(error);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("WebException: " + ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: " + ex.Message);
                }

                // Remove extra (null) UPS Services values slots from the array.
                shopRateResponse.UPSServices = shopRateResponse.UPSServices.Where(x => x != null).ToArray();

                return shopRateResponse;
            }           
        }

        /// <summary>
        /// Builds the JSON shipment request for the UPS Rate API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private string RateRequest(Shipment shipment)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"RateRequest\":");

                sb.Append("{\"Request\":");
                sb.Append("{\"TransactionReference\":{\"CustomerContext\": \"CustomerContext\"}"); // TransactionReference
                sb.Append("},"); // Request

                sb.Append("\"Shipment\":");
                    sb.Append("{\"Shipper\":");
                        sb.Append("{\"Name\": \"" + Configuration.UPSShipFromName + "\",");
                        sb.Append("\"ShipperNumber\": \"" + Configuration.ShipFromShipperNumber + "\",");
                        sb.Append("\"Address\":");
                            sb.Append("{\"AddressLine\": [");
                                sb.Append("\"" + Configuration.UPSShipFromAddress + "\",");
                                sb.Append("\"\",");
                                sb.Append("\"\"");
                            sb.Append("],"); // AddressLine
                            sb.Append("\"City\": \"" + Configuration.UPSShipFromCity + "\",");
                            sb.Append("\"StateProvinceCode\": \"" + Configuration.UPSShipFromState + "\",");
                            sb.Append("\"PostalCode\": \"" + Configuration.UPSShipFromZip + "\",");
                            sb.Append("\"CountryCode\": \"US\"");
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
                        sb.Append("\"City\": \""+ shipment.City + "\",");
                        sb.Append("\"StateProvinceCode\": \"" + shipment.State + "\",");
                        sb.Append("\"PostalCode\": \"" + shipment.Zip + "\",");
                        sb.Append("\"CountryCode\": \"US\"");
                    sb.Append("}"); // Address
                sb.Append("},"); // ShipTo

            sb.Append("\"ShipFrom\":");
                sb.Append("{\"Name\": \"" + Configuration.UPSShipFromName + "\",");
                sb.Append("\"Address\":");
                    sb.Append("{\"AddressLine\": [");
                        sb.Append("\"" + Configuration.UPSShipFromAddress + "\",");
                        sb.Append("\"\",");
                        sb.Append("\"\"");
                        sb.Append("],");
                    sb.Append("\"City\": \"" + Configuration.UPSShipFromCity + "\",");
                sb.Append("\"StateProvinceCode\": \"" + Configuration.UPSShipFromState + "\",");
                sb.Append("\"PostalCode\": \"" + Configuration.UPSShipFromZip + "\",");
                sb.Append("\"CountryCode\": \"US\"");
                sb.Append("}"); // Address
            sb.Append("},"); // ShipFrom

            sb.Append("\"ShipmentRatingOptions\": {");
                sb.Append("\"TPFCNegotiatedRatesIndicator\": \"Y\",");
                sb.Append("\"NegotiatedRatesIndicator\": \"Y\"");
            sb.Append("},"); // ShipmentRatingOptions

            sb.Append("\"Service\":");                
                sb.Append("{\"Code\": \"03\",");
                sb.Append("\"Description\": \"UPS Worldwide Economy DDU\"");
            sb.Append("},"); // Service


            sb.Append("\"NumOfPieces\": \"" + shipment.number_of_packages + "\",");
            sb.Append("\"Package\":");
                sb.Append("{\"PackagingType\":");
                    sb.Append("{\"Code\": \"02\",");
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
                sb.Append("\"OversizeIndicator\": \"X\",");
                sb.Append("\"MinimumBillableWeightIndicator\": \"X\"");
            sb.Append("}"); // RateRequest.Shipment.Package
            sb.Append("}"); // RateRequest.Shipment
            sb.Append("}"); // RateRequest
            sb.Append("}"); // ROOT
            return sb.ToString();
        }        


        /// <summary>
        /// Builds the JSON address request for the UPS API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private string AddressValidationRequest(Shipment shipment)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("{\"XAVRequest\":");
            stringBuilder.Append("{\"AddressKeyFormat\": ");
            stringBuilder.Append("{\"ConsigneeName\": \"\",");
            stringBuilder.Append("\"BuildingName\": \"\",");
            stringBuilder.Append("\"AddressLine\": [\"" + shipment.Address + "\",\"\",\"\"],");  // Only using the first line of the array.
            stringBuilder.Append("\"Region\": \"" + shipment.City + "," + shipment.State + "," + shipment.Zip + "\",");
            stringBuilder.Append("\"PoliticalDivision2\": \"\",");
            stringBuilder.Append("\"PoliticalDivision1\": \"" + shipment.State + "\",");
            stringBuilder.Append("\"PostcodePrimaryLow\": \"" + shipment.Zip + "\",");
            stringBuilder.Append("\"PostcodeExtendedLow\": \"\",");
            stringBuilder.Append("\"Urbanization\": \"\",");
            stringBuilder.Append("\"CountryCode\": \"" + shipment.Country + "\"");
            stringBuilder.Append("}"); // Consignee
            stringBuilder.Append("}"); // AddressKeyFormat
            stringBuilder.Append("}"); // ROOT

            return stringBuilder.ToString();
        }
    }
}
