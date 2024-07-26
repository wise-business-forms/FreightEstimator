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
using System.Data.SqlClient;
using Microsoft.Win32.SafeHandles;
using System.Data;
using System.Drawing.Printing;
using System.Xml.Linq;
using Microsoft.Ajax.Utilities;

namespace AuthenticationServer.Controllers
{
    public class PlantController : Controller
    {
        private string _upsRequest = string.Empty;
        private string _upsResponse = string.Empty;

        // GET: Plant
        public ActionResult Index(string loc)
        {
            ViewBag.Param1 = loc;
            if(string.IsNullOrEmpty(loc) || !Configuration.PlantLocations.ContainsKey(loc.ToUpper()))
            {
                return View("Error");
            }

            if (loc == "ALP")
            {
                ViewBag.AccountMessage = "Required for Shipments from ALP\r\nor \"Rate from Multiple Locations\"";
            }

            ViewBag.States = Geography.States();
            ViewBag.Countries = Geography.Countries();
            

            var plantName = Configuration.PlantLocations[loc.ToUpper()];            
            var model = new Models.Shipment { };

            // Setup default values for the shipment
            model.PlantId = loc.ToUpper();
            model.PlantName = plantName;
            model.Country_selection = "United States";
            //model.pick_up_date = System.DateTime.Now.ToShortDateString();

            model.delivery_signature_required = Configuration.DeliverySignatureRequiredSelection;
            model.multiple_location_rate = new List<SelectListItem> { new SelectListItem { Text = "No", Value = "No" }, new SelectListItem { Text = "Yes", Value = "Yes" } };
            model.include_ground_rate = new List<SelectListItem> { new SelectListItem { Text = "Yes", Value = "Yes" }, new SelectListItem { Text = "No", Value = "No" } };
            model.include_ltl_rate = new List<SelectListItem> { new SelectListItem { Text = "No", Value = "No" }, new SelectListItem { Text = "Yes", Value = "Yes" } };

            model.freight_class = new List<SelectListItem>
            {
                new SelectListItem { Text = "50", Value = "50"},
                new SelectListItem { Text = "55", Value = "55"},
                new SelectListItem { Text = "60", Value = "60"},
                new SelectListItem { Text = "65", Value = "65"}

            };

            model.delivery_signature_required_selection = "No";
            model.multiple_location_rate_selection = "No";
            model.include_ground_rate_selection = "No";
            model.include_ltl_rate_selection = "No";

            return View(model);
        }

        [HttpPost]
        public ActionResult SubmitShipment(Shipment shipment)
        {
            //if (ModelState.IsValid)
            {
                
                // Save the shipment to the database
                return RedirectToAction("ShipmentConfirmation", shipment);
            }
            
            //return View("Index", shipment);
        }

        public ActionResult LoadAdditionalOptions(Shipment shipment)
        {
            return PartialView("_AdditionalOptions", shipment);
        }

        public ActionResult ShipmentConfirmation(Shipment shipment)
        {
            shipment.billing_weight = shipment.number_of_packages * shipment.package_weight;

            shipment.shopRateResponse = ShopRateResponse(shipment); 
            shipment.requestMessage = _upsRequest;
            shipment.responseMessage = _upsResponse;
            shipment.LTLServices = new List<LTLService>();

            if (shipment.include_ltl_rate_selection == "Yes") { ShowLTLRates(shipment); }

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
                StateIsValid = (shipment.State_selection.Length >= 2);
            }
            else
            {
                StateIsValid = true;
            }
        }

        private void ShowLTLRates(Shipment shipment)
        {
            try
            {
                #region -- Define Per Package Charge and Per Shipment charge dictionaries for LTL (chare per plant) --
                Dictionary<string, double> dPerPackageChargeLTL = new Dictionary<string, double>();
                Dictionary<string, double> dPerShipmentChargeLTL = new Dictionary<string, double>();
                Dictionary<string, double> dUpchargeLTL = new Dictionary<string, double>();

                SqlConnection sqlConnection = new SqlConnection(Configuration.UpsRateSqlConnection);
                sqlConnection.Open();

                SqlCommand cmdCharges = sqlConnection.CreateCommand();
                cmdCharges.CommandText = "GetPlantCharges";
                cmdCharges.CommandType = System.Data.CommandType.StoredProcedure;
                cmdCharges.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = "M33";
                cmdCharges.Parameters.Add("@AcctNumber", System.Data.SqlDbType.Int).Value = shipment.AcctNum;

                SqlDataReader drCharges = cmdCharges.ExecuteReader();

                while (drCharges.Read())
                {
                    dPerPackageChargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["PerPackageCharge"].ToString()));
                    dPerShipmentChargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["PerShipmentCharge"].ToString()));
                    dUpchargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["Ground"].ToString()));
                }
                #endregion

                StringBuilder sbResults = new StringBuilder();
                string url = Configuration.TransPlaceUrl;
                string token = Configuration.TransPlaceToken;
                string fullPostData = "";
                string pickupDate = "";

                try
                {
                    DateTime datePickup = DateTime.Parse(shipment.pick_up_date.ToString());
                    pickupDate = datePickup.Year.ToString() + "-" + datePickup.Month.ToString() + "-" + datePickup.Day.ToString();
                }
                catch
                {
                    pickupDate = DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString() + "-" + DateTime.Now.Day.ToString();
                }
                string ltlClass = shipment.freight_class_selected.ToString();

                #region -- Define dtLTLServices to hold rate data --
                DataSet dsLTLServices = new DataSet();
                DataTable dtLTLServices = dsLTLServices.Tables.Add();

                dtLTLServices.Columns.Add("Plant", typeof(string));
                dtLTLServices.Columns.Add("Service", typeof(string));
                dtLTLServices.Columns.Add("Rate", typeof(double));
                dtLTLServices.Columns.Add("TransitDays", typeof(int));
                dtLTLServices.Columns.Add("Direct", typeof(string));
                #endregion

                string[] plantCodes = { "" };

                if(shipment.PlantId == "ALL")
                {
                    plantCodes = Configuration.PlantCodesMultiRate;
                }
                else
                {
                    plantCodes[0] = shipment.PlantId;
                }

                string combinedResponses = "";
                foreach (string plantCode in plantCodes)
                {
                    WebRequest request = WebRequest.Create(url + "rate/quote?loginToken=" + token);
                    request.Method = "POST";

                    #region -- Load Plant Ship From Address --
                    string shipFromCity = "";
                    string shipFromState = "";
                    string shipFromZip = "";
                    string shipFromCountry = "";

                    SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection);
                    conn.Open();

                    SqlCommand sqlCommand = conn.CreateCommand();
                    sqlCommand.Connection = conn;
                    sqlCommand.CommandType = CommandType.Text;
                    sqlCommand.CommandText = "SELECT city, State, Zip Country, FROM Plants WHERE Plantcode = '" + plantCode + "'";

                    SqlDataReader drResults = sqlCommand.ExecuteReader();

                    if (drResults.Read())
                    {
                        shipFromCity = drResults["City"].ToString();
                        shipFromState = drResults["State"].ToString();
                        shipFromZip = drResults["Zip"].ToString();
                        shipFromCountry = drResults["Country"].ToString();
                    }
                    else { 
                        sbResults.Append("Unable to lookup address info for Plant " + plantCode + "'");
                    }

                    conn.Close();

                    #endregion

                    StringBuilder postData = new StringBuilder("<?xml version=\"1.0\"?>");
                    postData.Append("<quote>");
                    postData.Append("< requestedMode>LTL</requestedMode>");
                    postData.Append("<requestedPickupDate>" + pickupDate + "</requestedPickupDate>");
                    postData.Append("<shipper>");
                    postData.Append("<city>" + shipFromCity + "</city>");
                    postData.Append("<region>" + shipFromState + "</region>");
                    postData.Append("<country>" + shipFromCountry + "</country>");
                    postData.Append("<postalCode>" + shipFromZip + "</postalCode>");
                    postData.Append("</shipper>");
                    postData.Append("<consignee>");
                    postData.Append("<city>" + shipment.City + "</city>");
                    postData.Append("<region>" + shipment.State_selection + "</region>");
                    postData.Append("<country>" + shipment.Country_selection + "</country>");
                    postData.Append("<postalCode>" + shipment.Zip + "</postalCode>");
                    postData.Append("</consignee>");
                    postData.Append("<lineItems>");
                    postData.Append("<lineItem>");
                    postData.Append("<freightClass>" + ltlClass + "</freightClass>");
                    postData.Append("<weight>" + shipment.billing_weight + "</weight>");
                    postData.Append("<weightUnit>LB</weightUnit>");
                    postData.Append("</lineItem>");
                    postData.Append("</lineItems>");

                    string accessorials = GetLTLAccessorials(shipment);
                    if (accessorials.Length > 0)
                    {
                        postData.Append("<accessorials>");
                        string[] accessorialArray = accessorials.Split(';');
                        for (int i = 0; i < accessorialArray.Count(); i++)
                        {
                            postData.Append("<accessorial><type>" + accessorialArray[i] + "</type></accessorial>");
                        }
                        postData.Append("</accessorials>");
                    }
                    //postData += "<accessorials>";
                    //postData += "<accessorial>";
                    //postData += "<type>LIFTGATE-PICKUP</type>";
                    //postData += "</accessorial>";
                    //postData += "</accessorials>";
                    postData.Append("</quote>");

                    fullPostData += postData;

                    byte[] byteArray = Encoding.UTF8.GetBytes(postData.ToString());
                    // Set the ContentType property of the WebRequest.
                    request.ContentType = "text/xml";
                    // Set the ContentLength property of the WebRequest.
                    request.ContentLength = byteArray.Length;
                    // Get the request stream.
                    Stream dataStream = request.GetRequestStream();
                    // Write the data to the request stream.
                    dataStream.Write(byteArray, 0, byteArray.Length);
                    // Close the Stream object.
                    dataStream.Close();

                    StringBuilder results = new StringBuilder(postData.ToString());
                    results.Append("\n\n\n");
                    results.Append("<br/>" + url + "rate/quote?loginToken=" + token);
                    results.Append("\n\n\n");
                    // Get the response.
                    WebResponse WebResponse = request.GetResponse();
                    // Display the status.
                    //Console.WriteLine(((HttpWebResponse)WebResponse).StatusDescription);
                    // Get the stream containing content returned by the server.
                    dataStream = WebResponse.GetResponseStream();
                    // Open the stream using a StreamReader for easy access.
                    StreamReader reader = new StreamReader(dataStream);
                    // Read the content.
                    string responseFromServer = reader.ReadToEnd();
                    // Display the content.
                    results.Append(responseFromServer);
                    results.Append("\n\n\n");
                    // Clean up the streams.
                    reader.Close();
                    dataStream.Close();
                    WebResponse.Close();

                    combinedResponses += responseFromServer;


                    XDocument xmlDoc = XDocument.Parse(responseFromServer);

                    foreach (var rate in xmlDoc.Descendants("rate"))
                    {
                        string carrier = rate.Element("carrier").Element("name").Value.Trim();
                        string direct = rate.Element("direct").Value.Trim();
                        int transitDays = Convert.ToInt16(rate.Element("transitDays").Value.Trim());
                        double cost = double.Parse(rate.Element("cost").Element("totalAmount").Value.Trim());
                        double totalCharges = 0;

                        #region -- Define variables for markup calculations --
                        double markupPercentage = 0;
                        double perPackageCharge = 0;
                        double perShipmentCharge = 0;
                        #endregion

                        results.Append("Cost is " + cost.ToString() + "\n");
                        results.Append("Markup percentage is " + markupPercentage.ToString() + "\n");
                        results.Append("Number of Packages is " + shipment.number_of_packages + "\n");
                        results.Append("Per package charge is " + perPackageCharge.ToString() + "\n");
                        results.Append("Per shipment charge is " + perShipmentCharge.ToString() + "\n");

                        totalCharges = cost;
                        totalCharges += ((markupPercentage / 100) * cost);
                        totalCharges += (perPackageCharge * shipment.number_of_packages) + perShipmentCharge;

                        results.Append("Calculated total charge is " + totalCharges.ToString() + "\n\n");

                        //if ((carrier != "LTL BENCHMARK") || (Session["DefaultPlant"].ToString() == "POR"))
                        if (carrier != "LTL BENCHMARK")
                        {
                            shipment.LTLServices.Add(new LTLService
                            {
                                PlantCode = plantCode,
                                Carrier = carrier,
                                TotalCharges = totalCharges,
                                TransitDays = transitDays,
                                Direct = direct
                            });

                        }
                        else if (Session["DefaultPlant"].ToString() == "POR")
                        {
                            shipment.LTLServices.Add(new LTLService
                            {
                                PlantCode = plantCode,
                                Carrier = carrier,
                                Cost = cost,
                                TransitDays = transitDays,
                                Direct = direct
                            });
                        }
                    }
                }
            

                #region -- log results --
                try
                {
                    string UserName = "TBD";

                    SqlConnection conlog = new SqlConnection(Configuration.UpsRateSqlConnection);
                    conlog.Open();

                    SqlCommand cmdLog = new SqlCommand();
                    cmdLog.Connection = conlog;
                    cmdLog.CommandType = CommandType.StoredProcedure;
                    cmdLog.CommandText = "LogResultsLTL";

                    SqlParameter pPlantCode = new SqlParameter("@PlantCode", SqlDbType.VarChar, 10);
                    SqlParameter pUserName = new SqlParameter("@UserName", SqlDbType.VarChar, 10);
                    SqlParameter pFullRequest = new SqlParameter("@FullRequest", SqlDbType.NText);
                    SqlParameter pFullResults = new SqlParameter("@FullResults", SqlDbType.NText);
                    SqlParameter pXmlResponse = new SqlParameter("@XmlResponse", SqlDbType.NText);

                    pPlantCode.Value = shipment.PlantId;
                    pUserName.Value = UserName;
                    pFullRequest.Value = shipment.requestMessage;
                    pFullResults.Value = shipment.responseMessage;
                    pXmlResponse.Value = string.Empty;

                    cmdLog.Parameters.Add(pPlantCode);
                    cmdLog.Parameters.Add(pUserName);
                    cmdLog.Parameters.Add(pFullRequest);
                    cmdLog.Parameters.Add(pFullResults);
                    cmdLog.Parameters.Add(pXmlResponse);

                    cmdLog.ExecuteNonQuery();
                    conlog.Close();
                }
                catch(Exception ex)
                {
                    // TODO
                }
                #endregion
            }
            catch (Exception ex)
            {
                //TODO
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
                        _upsRequest = RateRequest(shipment);
                        streamWriter.Write(_upsRequest);
                    }

                    // Get the response
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (var streamReader = new StreamReader(response.GetResponseStream()))
                            {
                                _upsResponse = streamReader.ReadToEnd();

                                dynamic data = JObject.Parse(_upsResponse);

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
                                _upsResponse = error;
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
                            sb.Append("\"CountryCode\": \"" + shipment.Country_selection + "\"");
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
                        sb.Append("\"StateProvinceCode\": \"" + shipment.State_selection + "\",");
                        sb.Append("\"PostalCode\": \"" + shipment.Zip + "\",");
                        sb.Append("\"CountryCode\": \"" + shipment.Country_selection + "\"");
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
                sb.Append("\"CountryCode\": \""+ shipment.Country_selection + "\"");
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
            stringBuilder.Append("\"Region\": \"" + shipment.City + "," + shipment.State_selection + "," + shipment.Zip + "\",");
            stringBuilder.Append("\"PoliticalDivision2\": \"\",");
            stringBuilder.Append("\"PoliticalDivision1\": \"" + shipment.State_selection + "\",");
            stringBuilder.Append("\"PostcodePrimaryLow\": \"" + shipment.Zip + "\",");
            stringBuilder.Append("\"PostcodeExtendedLow\": \"\",");
            stringBuilder.Append("\"Urbanization\": \"\",");
            stringBuilder.Append("\"CountryCode\": \"" + shipment.Country_selection + "\"");
            stringBuilder.Append("}"); // Consignee
            stringBuilder.Append("}"); // AddressKeyFormat
            stringBuilder.Append("}"); // ROOT

            return stringBuilder.ToString();
        }

        private string GetLTLAccessorials(Shipment shipment)
        {
            StringBuilder fullList = new StringBuilder();

            if(shipment.notify_before_delivery)
            {
                fullList.Append("NOTIFY-BEFORE-DELIVERY;");
            }
            if (shipment.liftgate_pickup)
            {
                fullList.Append("LIFTGATE-PICKUP;");
            }
            if (shipment.liftgate_delivery)
            {
                fullList.Append("LIFTGATE-DELIVERY;");
            }
            if (shipment.limited_access_pickup)
            {
                fullList.Append("LIMITED-ACCESS-PICKUP;");
            }
            if (shipment.limited_access_delivery)
            {
                fullList.Append("LIMITED-ACCESS-DELIVERY;");
            }
            if (shipment.residential_pickup)
            {
                fullList.Append("RESIDENTIAL-PICKUP;");
            }
            if (shipment.residential_delivery)
            {
                fullList.Append("RESIDENTIAL-DELIVERY;");
            }
            if (shipment.inside_pickup)
            {
                fullList.Append("INSIDE-PICKUP;");
            }
            if (shipment.inside_delivery)
            {
                fullList.Append("INSIDE-DELIVERY;");
            }
            if (shipment.sort_and_segregate)
            {
                fullList.Append("SORT-AND-SEGREGATE;");
            }
            if (shipment.stopoff_charge)
            {
                fullList.Append("STOPOFF-CHARGE;");
            }
            return fullList.ToString();
        }
    }
}
