﻿using AuthenticationServer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Web.Mvc;

namespace AuthenticationServer.Controllers
{
    public class PlantController : Controller
    {
        // GET: Plant
        public ActionResult Index(string loc)
        {
            ViewBag.Param1 = loc;
            var model = new Models.Shipment { };

            return View(model);
        }

        [HttpPost]
        public ActionResult SubmitShipment(Shipment shipment)
        {
            if (ModelState.IsValid)
            {
                // Save the shipment to the database
                return RedirectToAction("ShipmentConfirmation");
            }
            return View("Shipment", shipment);
        }

        public ActionResult ShipmentConfirmation(Shipment shipment)
        {
            //ValidateStateForCWT(shipment);            
            AddressKeyFormat response = AddressValidation(shipment);

            shipment.Address = response.AddressLine;
            shipment.City = response.PoliticalDivision2;
            shipment.State = response.PoliticalDivision1;
            shipment.Zip = response.PostcodePrimaryLow;

            shipment.billing_weight = shipment.number_of_packages * shipment.package_weight;

            ShopRateResponse(shipment);

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
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetToken());

                JObject jObjShipment = new JObject(
                    new JProperty("XAVRequest",
                        new JObject(
                            new JProperty("AddressKeyFormat",
                                new JObject(
                                    new JProperty("ConsigneeName", ""),
                                    new JProperty("BuildingName", ""),
                                    new JProperty("AddressLine", new JArray("1600 Pensenvalyia Ave", "", "")),
                                    new JProperty("Region", "Washington,DC,20500"),
                                    new JProperty("PoliticalDivision2", "ALISO VIEJO"),
                                    new JProperty("PoliticalDivision1", "CA"),
                                    new JProperty("PostcodePrimaryLow", "92656"),
                                    new JProperty("PostcodeExtendedLow", "1521"),
                                    new JProperty("Urbanization", "porto arundal"),
                                    new JProperty("CountryCode", "US")
                                )
                            )
                        )
                    )
                );
                /*
                JObject jObject = JObject.Parse(@"{
                XAVRequest: {
                    AddressKeyFormat: {
                        ConsigneeName: '',
                        BuildingName: '',
                        AddressLine: [
                            '1600 Pensenvalyia Ave',
                            '',
                            ''
                        ],
                        Region: 'Washington,DC,20500',
                        PoliticalDivision2: 'ALISO VIEJO',
                        PoliticalDivision1: 'CA',
                        PostcodePrimaryLow: '92656',
                        PostcodeExtendedLow: '1521',
                        Urbanization: 'porto arundal',
                        CountryCode: 'US'
                    }
                }
                }");
                */
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
                        streamWriter.Write(jObjShipment);
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
                    string s = RateRequest(shipment);
                    streamWriter.Write(RateRequest(shipment));
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

            return shopRateResponse;
        }

        /// <summary>
        /// Builds the JSON shipment request for the UPS Rate API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private string RateRequest(Shipment shipment)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("RateRequest\":");
            sb.Append("{\"Request\":");
            sb.Append("{\"TransactionReference\":");
            sb.Append("{\"CustomerContext\": \"CustomerContext\"}");
            sb.Append("},"); // TransactionReference
            sb.Append("\"Shipment\":");
            sb.Append("{\"Shipper\":");
            sb.Append("{\"Name\": \"" + Configuration.UPSShipFromName + "\",");
            sb.Append("\"ShipperNumber\": \"" + Configuration.ShipFromShipperNumber + "\",");
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
            sb.Append("}");
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
            sb.Append("}");
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
            sb.Append("}");
            sb.Append("},"); // ShipFrom
            sb.Append("\"PaymentDetails\":");
            sb.Append("{\"ShipmentCharge\":");
            sb.Append("{\"Type\": \"01\",");
            sb.Append("\"BillShipper\":");
            sb.Append("{\"AccountNumber\": \"" + Configuration.ShipFromShipperNumber + "\"}");
            sb.Append("}");
            sb.Append("},"); // PaymentDetails
            sb.Append("\"Service\":");                
            sb.Append("{\"Code\": \"03\",");
            sb.Append("\"Description\": \"Ground\"");
            sb.Append("},"); // Service
            sb.Append("\"NumOfPieces\": \"" + shipment.number_of_packages + "\",");
            sb.Append("\"Package\":");
            sb.Append("{\"SimpleRate\":");
            sb.Append("{\"Description\": \"SimpleRateDescription\",");
            sb.Append("\"Code\": \"XS\"");
            sb.Append("},");
            sb.Append("\"PackagingType\":");
            sb.Append("{\"Code\": \"02\",");
            sb.Append("\"Description\": \"Packaging\"");
            sb.Append("},");
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
            sb.Append("\"Description\": \"Pounds\"");
            sb.Append("},");
            sb.Append("\"Weight\": \"" + shipment.billing_weight + "\"");
            sb.Append("}");
            sb.Append("}");
            sb.Append("}");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
