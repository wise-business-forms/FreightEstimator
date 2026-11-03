using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace AuthenticationServer.Models.Services
{
    /// <summary>The Logistics Partner accessorials a shipment can ask for.</summary>
    public class LtlAccessorials
    {
        public bool NotifyBeforeDelivery { get; set; }
        public bool LiftgatePickup { get; set; }
        public bool LiftgateDelivery { get; set; }
        public bool LimitedAccessPickup { get; set; }
        public bool LimitedAccessDelivery { get; set; }
        public bool ResidentialPickup { get; set; }
        public bool ResidentialDelivery { get; set; }
        public bool InsidePickup { get; set; }
        public bool InsideDelivery { get; set; }
        public bool SortAndSegregate { get; set; }
        public bool StopoffCharge { get; set; }
    }

    /// <summary>
    /// What a caller asks the Logistics Partner estimation service for: one shipment from one Plant. A plain
    /// request: the service never writes to it (see CONTEXT.md: Quote, Carrier Rate, Calculated Rate, Plant Charges).
    /// </summary>
    public class LogisticsPartnerQuoteRequest
    {
        /// <summary>The Plant the shipment ships from. Named explicitly; never inferred.</summary>
        public string PlantCode { get; set; }
        public Guid QuoteId { get; set; }

        /// <summary>Carried for the Quote API; persisted with the Quote by the Quote logging ticket (#18), not yet used.</summary>
        public string ReferenceNumber { get; set; }
        /// <summary>The Consumer that asked, or null for the Plant page. Persisted by #18, not yet used.</summary>
        public string Consumer { get; set; }
        public string UserName { get; set; }

        /// <summary>
        /// The real account number, as typed on the Plant page. Plant Charges are looked up for it (UPS quotes use
        /// account 0), and a blank one is treated as "0".
        /// </summary>
        public string AccountNumber { get; set; }

        /// <summary>
        /// The destination city UPS's address check corrected, or null when UPS corrected nothing. The Logistics
        /// Partner is sent this and no other city, so a destination UPS did not correct goes out with an empty city.
        /// Pinned as the Plant page behaves today.
        /// </summary>
        public string CorrectedCity { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string Country { get; set; }

        public int NumberOfPackages { get; set; }
        public float PackageWeight { get; set; }
        public float LastPackageWeight { get; set; }
        public int FreightClass { get; set; }
        public DateTime PickupDate { get; set; }
        public LtlAccessorials Accessorials { get; set; } = new LtlAccessorials();
    }

    /// <summary>
    /// What the Logistics Partner call learned about, or changed on, the shipment: the defaults it applies and the
    /// request and response it exchanged. The caller decides what to do with them; the service does not write them
    /// back to the request.
    /// </summary>
    public class LogisticsPartnerOutcome
    {
        public string AccountNumber { get; set; }
        public string Country { get; set; }
        public string RequestMessage { get; set; }
        public string ResponseMessage { get; set; }
    }

    public class LogisticsPartnerQuoteResult
    {
        /// <summary>The Logistics Partner's services with Plant Charges applied. UPSServices is null when the Logistics Partner answered with an error status.</summary>
        public ShopRateResponse Rates { get; set; }
        public LogisticsPartnerOutcome Outcome { get; set; }
    }

    /// <summary>
    /// Quotes the Logistics Partner (LTL) for one shipment from one Plant. Extracted unchanged from
    /// PlantController (#17): same Logistics Partner request, same Calculated Rate formula, same Plant Charges
    /// lookup (including the real account number), same logging. The path writes to the shipment it is given
    /// (account default, country, request and response messages), so the service gives it a private working copy
    /// and reports the result in <see cref="LogisticsPartnerOutcome"/>.
    /// </summary>
    public class LogisticsPartnerEstimationService
    {
        public LogisticsPartnerQuoteResult Quote(LogisticsPartnerQuoteRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            Shipment shipment = WorkingShipment(request);
            ShopRateResponse rates = GetRates(shipment);

            return new LogisticsPartnerQuoteResult
            {
                Rates = rates,
                Outcome = new LogisticsPartnerOutcome
                {
                    AccountNumber = shipment.AcctNum,
                    Country = shipment.Country_selection,
                    RequestMessage = shipment.requestMessage,
                    ResponseMessage = shipment.responseMessage
                }
            };
        }

        private static Shipment WorkingShipment(LogisticsPartnerQuoteRequest request)
        {
            LtlAccessorials a = request.Accessorials ?? new LtlAccessorials();
            return new Shipment
            {
                PlantId = request.PlantCode,
                QuoteId = request.QuoteId,
                user_name = request.UserName,
                AcctNum = request.AccountNumber,
                Corrected_City = request.CorrectedCity,
                State_selection = request.State,
                Zip = request.Zip,
                Country_selection = request.Country,
                number_of_packages = request.NumberOfPackages,
                package_weight = request.PackageWeight,
                last_package_weight = request.LastPackageWeight,
                freight_class_selected = request.FreightClass,
                pick_up_date = request.PickupDate,
                notify_before_delivery = a.NotifyBeforeDelivery,
                liftgate_pickup = a.LiftgatePickup,
                liftgate_delivery = a.LiftgateDelivery,
                limited_access_pickup = a.LimitedAccessPickup,
                limited_access_delivery = a.LimitedAccessDelivery,
                residential_pickup = a.ResidentialPickup,
                residential_delivery = a.ResidentialDelivery,
                inside_pickup = a.InsidePickup,
                inside_delivery = a.InsideDelivery,
                sort_and_segregate = a.SortAndSegregate,
                stopoff_charge = a.StopoffCharge
            };
        }

        /// <summary>
        /// Persists a Logistics Partner (LTL) service's Calculated Rate + markup breakdown for
        /// reporting. See UpsEstimationService.LogCalculation for why construction is inside the try.
        /// </summary>
        private static void LogCalculation(
            Shipment shipment, UPSService service,
            double carrierRate, double perPackageCharge, double perShipmentCharge, double markupFraction, double calculatedRate)
        {
            try
            {
                Log.LogQuoteServiceCalculation(QuoteServiceCalculation.FromLogisticsPartner(
                    shipment, service, carrierRate, perPackageCharge, perShipmentCharge, markupFraction, calculatedRate));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("LogLogisticsPartnerQuoteServiceCalculation failed: " + ex.Message);
            }
        }

        /// <summary>
        /// GRID 3 - Less Than Truckload (LTL) Rates
        /// Uses the Transplace API
        /// </summary>
        /// <param name="shipment"></param>
        /// <returns></returns>
        private static ShopRateResponse GetRates(Shipment shipment)
        {
            float PlantUpcharges = GetPlantUpcharges(shipment, "TI");
            double plantSurcharge = 0;
            double plantPackageCharge = 0;
            double plantShipmentCharge = 0;
            ShopRateResponse response = new ShopRateResponse();


            if (string.IsNullOrWhiteSpace(shipment.AcctNum)) shipment.AcctNum = "0";
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
                cmdCharges.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = "TI";
                cmdCharges.Parameters.Add("@AcctNumber", System.Data.SqlDbType.Int).Value = shipment.AcctNum;

                SqlDataReader drCharges = cmdCharges.ExecuteReader();

                while (drCharges.Read())
                {
                    dPerPackageChargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["PerPackageCharge"].ToString()));
                    dPerShipmentChargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["PerShipmentCharge"].ToString()));
                    dUpchargeLTL.Add(drCharges["PlantCode"].ToString(), Convert.ToDouble(drCharges["Ground"].ToString()));
                }
                if (dUpchargeLTL[shipment.PlantId] > 0)
                {
                    plantSurcharge = dUpchargeLTL[shipment.PlantId];
                    plantSurcharge = (plantSurcharge / 100);
                }

                if (dPerPackageChargeLTL[shipment.PlantId] > 0)
                {
                    plantPackageCharge = dPerPackageChargeLTL[shipment.PlantId];
                }

                if (dPerShipmentChargeLTL[shipment.PlantId] > 0)
                {
                    plantShipmentCharge = dPerShipmentChargeLTL[shipment.PlantId];
                }

                #endregion

                StringBuilder sbResults = new StringBuilder();
                string url = Configuration.TransportInsightRateUrl;
                string userid = Configuration.TransportInsightRateUserId;
                string password = Configuration.TransportInsightRateUserPassword;
                string fullPostData = "";
                string pickupDate = "";

                try
                {
                    DateTime datePickup = shipment.pick_up_date;
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

                if (shipment.PlantId == "ALL")
                {
                    plantCodes = Configuration.PlantCodesMultiRate;
                }
                else
                {
                    plantCodes[0] = shipment.PlantId;
                }

                string combinedResponses = "";
                List<UPSService> ltlServices = new List<UPSService>();
                string xmlResponse = string.Empty;

                foreach (string plantCode in plantCodes)
                {
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
                    sqlCommand.CommandText = "SELECT city, State, Zip, Country FROM Plants WHERE Plantcode = '" + plantCode + "'";

                    SqlDataReader drResults = sqlCommand.ExecuteReader();

                    if (shipment.Country_selection == "US")
                    {
                        shipment.Country_selection = "USA";
                    }

                    if (drResults.Read())
                    {
                        shipFromCity = drResults["City"].ToString();
                        shipFromState = drResults["State"].ToString();
                        shipFromZip = drResults["Zip"].ToString();
                        shipFromCountry = drResults["Country"].ToString().Replace("US", "USA");
                    }
                    else
                    {
                        sbResults.Append("Unable to lookup address info for Plant " + plantCode + "'");
                    }

                    conn.Close();

                    #endregion

                    #region Build XML Request
                    StringBuilder request = new StringBuilder();
                    request.Append("<?xml version=\"1.0\"?>");
                    request.Append("<service-request xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
                    request.Append("<service-id>XMLRating</service-id>");
                    request.Append("<request-id>123456789</request-id>");
                    request.Append("<data>");
                        request.Append("<RateRequest><RatingLevel isCompanyAccountNumber=\"true\">WISE03RATE</RatingLevel>");
                        request.Append("<Constraints>");
                            request.Append("<PaymentTerms>Prepaid</PaymentTerms>");

                            string accessorials = GetAccessorials(shipment);
                            if(accessorials.Length > 0)
                            {
                                request.Append("<ServiceFlags>");
                                request.Append(accessorials);
                                request.Append("</ServiceFlags>");
                            }
                            else
                            {
                                request.Append("<ServiceFlags />");
                            }
                            
                        request.Append("</Constraints>");
                        request.Append("<Items>");
                    if (shipment.last_package_weight > 0)
                    {
                        for (int i = 0; i < shipment.number_of_packages-1; i++)
                        {
                            request.Append("<Item sequence=\"1\" freightClass=\"");
                            // Sep 21, 2025 - Switching from FreightClass 55 to 85 per NMFC changes.
                            // Sep 26, 2025 - Switching from FreightClass 85 to 65 per feedback.
                            request.Append(ltlClass);
                            request.Append("\">");
                            request.Append("<Weight units=\"lb\">");
                            request.Append(shipment.package_weight.ToString());
                            request.Append("</Weight>");
                            request.Append("<Dimensions length=\"5.0\" width=\"5.0\" height=\"5.0\" units=\"in\" /></Item>");
                        }

                        // Add last package
                        request.Append("<Item sequence=\"1\" freightClass=\"");
                        // Sep 21, 2025 - Switching from FreightClass 55 to 85 per NMFC changes.
                        // Sep 26, 2025 - Switching from FreightClass 85 to 65 per feedback.
                        request.Append(ltlClass);
                        request.Append("\">");
                        request.Append("<Weight units=\"lb\">");
                        request.Append(shipment.last_package_weight.ToString());
                        request.Append("</Weight>");
                        request.Append("<Dimensions length=\"5.0\" width=\"5.0\" height=\"5.0\" units=\"in\" /></Item>");
                    }
                    else
                    {
                        for (int i = 0; i < shipment.number_of_packages; i++)
                        {
                            request.Append("<Item sequence=\"1\" freightClass=\"");
                            // Sep 21, 2025 - Switching from FreightClass 55 to 85 per NMFC changes.
                            // Sep 26, 2025 - Switching from FreightClass 85 to 65 per feedback.
                            request.Append(ltlClass);
                            request.Append("\">");
                            request.Append("<Weight units=\"lb\">");
                            request.Append(shipment.package_weight.ToString());
                            request.Append("</Weight>");
                            request.Append("<Dimensions length=\"5.0\" width=\"5.0\" height=\"5.0\" units=\"in\" /></Item>");
                        }
                    }
                        
                        request.Append("</Items>");

                    request.Append("<Events>");
                        request.Append("<Event sequence=\"1\" type=\"Pickup\" date=\"").Append(shipment.pick_up_date.ToString("MM/dd/yyyy HH:mm")).Append("\">");
                        request.Append("<Location><City>").Append(shipFromCity).Append("</City><State>").Append(shipFromState).Append("</State><Zip>").Append(shipFromZip).Append("</Zip><Country>").Append(shipFromCountry).Append("</Country></Location>");
                        request.Append("</Event>");

                        request.Append("<Event sequence=\"2\" type=\"Drop\" date=\"").Append(shipment.pick_up_date.ToString("MM/dd/yyyy HH:mm")).Append("\">");
                        request.Append("<Location><City>").Append(shipment.Corrected_City).Append("</City><State>").Append(shipment.State_selection).Append("</State><Zip>").Append(shipment.Zip).Append("</Zip><Country>").Append(shipment.Country_selection).Append("</Country></Location>");
                        request.Append("</Event>");
                    request.Append("</Events>");

                    request.Append("</RateRequest></data></service-request>");
                    #endregion

                    #region Add UserID/Pass & encode.
                    StringBuilder postData = new StringBuilder();
                    postData.Append("userid=").Append($"{Uri.EscapeDataString(Configuration.TransportInsightRateUserId)}");
                    postData.Append("&password=").Append($"{Uri.EscapeDataString(Configuration.TransportInsightRateUserPassword)}");
                    postData.Append("&request=").Append($"{Uri.UnescapeDataString(request.ToString())}");
                    #endregion

                    fullPostData += postData;

                    CarrierHttpRequest carrierRequest = new CarrierHttpRequest
                    {
                        Url = url,
                        ContentType = "application/x-www-form-urlencoded",
                        Body = postData.ToString()
                    };

                    shipment.requestMessage = postData.ToString();

                    StringBuilder results = new StringBuilder(postData.ToString());
                    results.Append("\n\n\n");
                    results.Append("<br/>" + url);
                    results.Append("\n\n\n");
                    // Get the response.
                    CarrierHttpResponse carrierResponse = CarrierHttp.Current.Post(carrierRequest);
                    if (carrierResponse.IsError)
                    {
                        // An error status used to throw to the WebException handler below, which logs it and returns the empty response.
                        Console.WriteLine($"Error: {carrierResponse.StatusCode}");
                        Console.WriteLine("Response Body: ");
                        Console.WriteLine(carrierResponse.Body);
                        return response;
                    }
                    string responseFromServer = carrierResponse.Body;
                    // Display the content.
                    results.Append(responseFromServer);
                    results.Append("\n\n\n");

                    combinedResponses += responseFromServer;
                    xmlResponse = responseFromServer;
                    shipment.responseMessage = responseFromServer;

                    #region Parse response
                    // Decode response.
                    XDocument xmlDoc = XDocument.Parse(responseFromServer);
                    string encodedData = xmlDoc.Descendants("data").FirstOrDefault()?.Value;
                    byte[] data = Convert.FromBase64String(encodedData);
                    string decodedString = System.Text.Encoding.UTF8.GetString(data);

                    // Parse the result.
                    XDocument decodedXmlDoc = XDocument.Parse(decodedString);
                    var priceSheets = decodedXmlDoc.Descendants("PriceSheet").Select((pricesheet, index) => new
                    {
                        PriceSheetIndex = index + 1,
                        CarrierName = pricesheet.Element("CarrierName")?.Value,
                        Rate = pricesheet.Element("SubTotal")?.Value,
                        TotalCost = pricesheet.Element("Total")?.Value,
                        TransitDays = pricesheet.Element("ServiceDays")?.Value,
                        FuelCharge = pricesheet
                            .Element("Charges")?
                            .Elements("Charge")
                            .FirstOrDefault(charge => (string)charge.Attribute("type") == "ACCESSORIAL_FUEL")
                            ?.Element("Amount")?.Value, // Adjust if you want a specific child node like <Amount>
                        TotalAccessorialAmount = pricesheet
                            .Element("Charges")?
                            .Elements("Charge")
                            .Where(charge => (string)charge.Attribute("type") == "ACCESSORIAL")
                            .Sum(charge => decimal.TryParse(charge.Element("Amount")?.Value, out var amount) ? amount : 0) // Sums all amounts
                        //AccessorialTotal = pricesheet.Element("AccessorialTotal")?.Value

                    });

                    foreach ( var priceSheet in priceSheets)
                    {
                        double totalCharges = Convert.ToDouble(priceSheet.TotalCost);

                        if(plantSurcharge > 0)
                        {
                            totalCharges += (plantSurcharge * Convert.ToDouble(priceSheet.TotalCost));
                        }

                        if(plantPackageCharge > 0)
                        {
                            totalCharges += (plantPackageCharge * shipment.number_of_packages);
                        }

                      totalCharges += plantShipmentCharge;

                        UPSService service = new UPSService();

                        service.PlantCode = plantCode;
                        service.ServiceName = priceSheet.CarrierName;
                        service.CustomerRate = double.Parse(priceSheet.TotalCost);
                        service.TotalCost = totalCharges.ToString("C");

                        service.TransitDays = priceSheet.TransitDays;
                        service.RatedShipment_Surcharge = (plantSurcharge * Convert.ToDouble(priceSheet.TotalCost)).ToString("C");
                        service.Plant_PerPackageCharge = (plantPackageCharge * shipment.number_of_packages).ToString("C");
                        service.Plant_ShipmentCharge = plantShipmentCharge.ToString("C");

                        service.RatedShipment_BaseServiceCharge_MonetaryValue = priceSheet.Rate;
                        service.RatedShipment_TransportationCharges_MonetaryValue = double.Parse(priceSheet.FuelCharge).ToString("C");
                        service.RatedShipment_AccessorialCharges_MonetaryValue = priceSheet.TotalAccessorialAmount.ToString();
                        service.RatedShipment_PublishedRateCharges_MonetaryValue = priceSheet.TotalCost;

                        ltlServices.Add(service);

                        LogCalculation(
                            shipment, service,
                            carrierRate: service.CustomerRate,
                            perPackageCharge: plantPackageCharge * shipment.number_of_packages,
                            perShipmentCharge: plantShipmentCharge,
                            markupFraction: plantSurcharge,
                            calculatedRate: totalCharges);
                        ShadowComparisonRecorder.Record(
                            callSite: "LTL",
                            quoteId: shipment.QuoteId,
                            plantCode: service.PlantCode,
                            chargeScheduleServiceName: RateCalculationsChargeSchedule.LtlServiceName,
                            displayServiceName: service.ServiceName,
                            packageCount: shipment.number_of_packages,
                            carrierRate: (decimal)service.CustomerRate,
                            isCwtEligible: false, // LTL has no CWT concept.
                            existingCalculatedRate: (decimal)totalCharges);
                    }

                    response.UPSServices = ltlServices.ToArray();

                    try
                    {
                        response.UPSServices = response.UPSServices
                            .Where(s => !string.IsNullOrWhiteSpace(s.TransitDays) && !string.IsNullOrWhiteSpace(s.RatedShipment_PublishedRateCharges_MonetaryValue)) // Filter out invalid rows
                            .OrderBy(s => float.TryParse(s.CustomerRate.ToString(), out float days) ? days : float.MinValue) // Parse TransitDays or use default for invalid values
                            .ThenBy(s => float.TryParse(s.RatedShipment_PublishedRateCharges_MonetaryValue, out float cost) ? cost : float.MinValue) // Parse TotalCharges or use default
                            .ToArray();
                    }
                    catch(Exception ex)
                    {
                        // If there is a problem returnthe list unsorted.
                        response.UPSServices = ltlServices.ToArray();
                    }
                    #endregion
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
                    pXmlResponse.Value = xmlResponse;

                    cmdLog.Parameters.Add(pPlantCode);
                    cmdLog.Parameters.Add(pUserName);
                    cmdLog.Parameters.Add(pFullRequest);
                    cmdLog.Parameters.Add(pFullResults);
                    cmdLog.Parameters.Add(pXmlResponse);

                    cmdLog.ExecuteNonQuery();
                    conlog.Close();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                #endregion
            }
            catch (WebException wex)
            {
                // Handle error response
                if (wex.Response is HttpWebResponse errorResponse)
                {
                    Console.WriteLine($"Error: {errorResponse.StatusCode}");
                    using (StreamReader reader = new StreamReader(errorResponse.GetResponseStream()))
                    {
                        string responseContent = reader.ReadToEnd();
                        Console.WriteLine("Response Body: ");
                        Console.WriteLine(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return response;
        }

        private static float GetPlantUpcharges(Shipment shipment, String carrier)
        {
            Shipment _shipment = shipment;
            int accountNumber = 0;
            if (Int32.TryParse(shipment.AcctNum, out accountNumber))
            {
                accountNumber = Int32.Parse(shipment.AcctNum);
            }
            Dictionary<string, string> PerPackageCharge = new Dictionary<string, string>();
            SqlConnection sqlConnection = new SqlConnection(Configuration.UpsRateSqlConnection);
            sqlConnection.Open();

            SqlCommand sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = "GetPlantCharges";
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.Parameters.Add("@Carrier", System.Data.SqlDbType.VarChar, 50).Value = carrier.ToString();
            sqlCommand.Parameters.Add("AcctNumber", System.Data.SqlDbType.Int).Value = accountNumber;

            using (SqlDataReader reader = sqlCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    if(shipment.PlantId == reader["PlantCode"].ToString())
                    {
                        float pkgcharges = shipment.number_of_packages * float.Parse(reader["PerPackageCharge"].ToString());
                        float shipmentCharge = float.Parse(reader["PerShipmentCharge"].ToString());
                        float serviceCharge = float.Parse(reader["Ground"].ToString());
                        return pkgcharges + shipmentCharge + serviceCharge;
                    }
                }
            }
            return 0;
        }

        private static string GetAccessorials(Shipment shipment)
        { 
            StringBuilder accessorials = new StringBuilder();

            // NPDEL	Notification Prior to Delivery
            if (shipment.notify_before_delivery) { accessorials.Append("<ServiceFlag code=\"NPDEL\" />");  }
            // LIFT	Liftgate Service
            if (shipment.liftgate_pickup) { accessorials.Append("<ServiceFlag code=\"LIFT\" />"); }
            if (shipment.liftgate_delivery) { accessorials.Append("<ServiceFlag code=\"LIFT\" />"); }
            // LAPD	Limited Access Delivery
            if (shipment.limited_access_pickup) { accessorials.Append("<ServiceFlag code=\"LAPD\" />"); }
            if (shipment.limited_access_delivery) { accessorials.Append("<ServiceFlag code=\"LAPD\" />"); }
            // RESD	Residential Delivery
            if (shipment.residential_pickup) { accessorials.Append("<ServiceFlag code=\"RESD\" />"); }
            if (shipment.residential_delivery) { accessorials.Append("<ServiceFlag code=\"RESD\" />"); }
            // IDEL	Inside Delivery
            if (shipment.inside_pickup) { accessorials.Append("<ServiceFlag code=\"IDEL\" />"); }
            if (shipment.inside_delivery) { accessorials.Append("<ServiceFlag code=\"IDEL\" />"); }
            // ???
            if (shipment.sort_and_segregate) { accessorials.Append("<ServiceFlag code=\"SORT\" />"); }
            // ???
            if (shipment.stopoff_charge) { accessorials.Append("<ServiceFlag code=\"STOPOFF\" />"); } // ? Not sure this is correct based on https://qa-api-docs.mercurygate.net/documentation/standard-data.html

            return accessorials.ToString();
        }
    }
}
