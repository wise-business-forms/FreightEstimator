using AuthenticationServer.Models;
using AuthenticationServer.Models.Reports;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Web.Mvc;

namespace AuthenticationServer.Controllers
{
    public class ReportsController : Controller
    {
        // GET: Reports
        public ActionResult Index()
        {
            return View();
        }

        // GET: Reports
        public ActionResult MarginReport()
        {
            List<Invoice> invoices = new List<Invoice>();

            try
            {
                SqlConnection connection = new SqlConnection(Configuration.DatawarehouseSqlConnection);
                SqlCommand cmd = connection.CreateCommand();
                StringBuilder stringBuilder = new StringBuilder("SELECT at2.SVIA_S, at2.AGNDT_S, '30', at2.BILLTO, at2.MALPHA_S , at2.INVNO, at2.FRGHT, sum(NetDue) AS NetDue ");
                stringBuilder.Append("FROM Freight_Margin_VIEW AS fm ");
                stringBuilder.Append("INNER JOIN ALPERP.PSLWise.dbo.ARSAL_T1 AS at2 ON ");
                stringBuilder.Append("at2.INVNO = fm.InvoiceNumber ");
                stringBuilder.Append("GROUP BY at2.SVIA_S, at2.AGNDT_S, at2.BILLTO, at2.MALPHA_S , at2.INVNO, at2.FRGHT");
                //stringBuilder.Append("WHERE at2.INVNO = '247290';");
                cmd.CommandText = stringBuilder.ToString();
                connection.Open();

                DbDataReader reader = cmd.ExecuteReader();
                DataTable dataTable = new DataTable();
                dataTable.Load(reader);

                reader.Close();
                connection.Close();

                foreach (DataRow row in dataTable.Rows)
                {
                    Invoice invoice = new Invoice();
                    invoice.Plant = new Plant("ALP");
                    invoice.Date = DateTime.Parse(row["AGNDT_S"].ToString());
                    invoice.SoldCustomerNumber = int.Parse(row["BILLTO"].ToString());
                    invoice.SoldCustomerName = row["MALPHA_S"].ToString();
                    invoice.InvoiceNumber = int.Parse(row["INVNO"].ToString());
                    invoice.AirBillProNumber = "-";
                    invoice.InvoiceFreightBilled = row["FRGHT"].ToString();
                    invoice.TotalCost = row["NETDUE"].ToString(); ; // SUM GroundCommercial, FuelSurcharge, and all accessorials.
                    invoice.FreightMargin = decimal.Parse(row["FRGHT"].ToString()) - decimal.Parse(row["NETDUE"].ToString()); // InvoiceFreightBilled - TotalCost
                    invoice.FreightMarkupPercentage = 0; // FreightMargin / TotalCost
                    invoice.GroundCommercial = row["NETDUE"].ToString();
                    invoice.FuelSurcharge = 0;
                    invoice.Accessorial1 = 0;
                    invoice.Accessorial2 = 0;
                    invoice.Accessorial3 = 0;
                    
                    invoices.Add(invoice);
                }
            }
            catch (Exception ex)
            {
                throw; // Propagate the exception for the caller to handle.
            }

            //try
            //{
            //    SqlCommand cmd = connection.CreateCommand();
            //    StringBuilder stringBuilder = new StringBuilder("SELECT CustomerRef, NetDue FROM Freight_Margin");
            //    cmd.CommandText = stringBuilder.ToString();
            //    connection.Open();

            //    DbDataReader reader = cmd.ExecuteReader();
            //    DataTable dataTable = new DataTable();
            //    dataTable.Load(reader);

            //    reader.Close();
            //    connection.Close();

            //    foreach (DataRow row in dataTable.Rows)
            //    {
            //        Invoice invoice = new Invoice();
            //        invoice.Plant = new Plant("ALP");
            //        invoice.Date = DateTime.Parse(row["AGNDT_S"].ToString());
            //        invoice.SoldCustomerNumber = int.Parse(row["BILLTO"].ToString());
            //        invoice.SoldCustomerName = row["MALPHA_S"].ToString();
            //        invoice.InvoiceNumber = int.Parse(row["INVNO"].ToString());
            //        invoice.AirBillProNumber = "-";
            //        invoice.InvoiceFreightBilled = 0;
            //        invoice.TotalCost = 0; // SUM GroundCommercial, FuelSurcharge, and all accessorials.
            //        invoice.FreightMargin = 0; // InvoiceFreightBilled - TotalCost
            //        invoice.FreightMarkupPercentage = 0; // FreightMargin / TotalCost
            //        invoice.GroundCommercial = 0;
            //        invoice.FuelSurcharge = 0;
            //        invoice.Accessorial1 = 0;
            //        invoice.Accessorial2 = 0;
            //        invoice.Accessorial3 = 0;

            //        invoices.Add(invoice);
            //    }
            //}
            //catch (Exception ex)
            //{
            //    throw; // Propagate the exception for the caller to handle.
            //}

            return View(invoices);

            //return View();
        }

        public const string AllPlants = "ALL";

        public ActionResult Quotes(int page = 1, int pageSize = 50, string search = null, string plant = null)
        {
            var records = new List<RateRequestRecord>();

            var plants = Plant.Plants();
            var plantLookup = plants
                .ToDictionary(
                    p => (p.City ?? "").Trim().ToUpperInvariant() + "|" + (p.State ?? "").Trim().ToUpperInvariant(),
                    p => p.Id,
                    StringComparer.OrdinalIgnoreCase);

            ViewBag.Plants = plants.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
            ViewBag.SelectedPlant = plant;
            ViewBag.SearchTerm = search;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;

            // Blank plant and no search: show nothing until the user picks a plant.
            bool hasSearch = !string.IsNullOrWhiteSpace(search);
            if (string.IsNullOrEmpty(plant) && !hasSearch)
            {
                ViewBag.TotalCount = 0;
                ViewBag.TotalPages = 0;
                return View(records);
            }

            var selectedPlant = plants.FirstOrDefault(p => string.Equals(p.Id, plant, StringComparison.OrdinalIgnoreCase));

            using (var conn = new SqlConnection(Configuration.UpsRateSqlConnection))
            {
                conn.Open();

                // Build WHERE clause for search and plant. Plant isn't stored on Requests_Rate, so
                // filter through RateRequestPlant (Database/003), catching it up with any new requests first.
                var conditions = new List<string>();
                if (hasSearch)
                {
                    conditions.Add("CAST(RequestId AS VARCHAR) LIKE @Search");
                }
                if (selectedPlant != null)
                {
                    using (var catchUpCmd = new SqlCommand("dbo.RecordRateRequestPlants", conn) { CommandType = CommandType.StoredProcedure })
                    {
                        catchUpCmd.ExecuteNonQuery();
                    }
                    conditions.Add("RequestId IN (SELECT RequestId FROM RateRequestPlant WHERE PlantCode = @PlantCode)");
                }
                else if (!string.IsNullOrEmpty(plant) && plant != AllPlants)
                {
                    conditions.Add("1 = 0");
                }
                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";

                Action<SqlCommand> addFilterParameters = c =>
                {
                    if (hasSearch)
                    {
                        c.Parameters.Add(new SqlParameter("@Search", SqlDbType.NVarChar) { Value = "%" + search.Trim() + "%" });
                    }
                    if (selectedPlant != null)
                    {
                        c.Parameters.Add(new SqlParameter("@PlantCode", SqlDbType.VarChar, 10) { Value = selectedPlant.Id });
                    }
                };

                var countCmd = new SqlCommand("SELECT COUNT(*) FROM Requests_Rate" + whereClause, conn);
                addFilterParameters(countCmd);
                int totalCount = (int)countCmd.ExecuteScalar();

                ViewBag.TotalCount = totalCount;
                ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);

                var cmd = new SqlCommand(
                    @"SELECT RequestId, UserName, Address, City, State, Zip, Country, RequestDate, ResponseXml, RequestXml
                      FROM Requests_Rate" + whereClause + @"
                      ORDER BY RequestDate DESC
                      OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", conn);

                addFilterParameters(cmd);
                cmd.Parameters.Add(new SqlParameter("@Skip", SqlDbType.Int) { Value = (page - 1) * pageSize });
                cmd.Parameters.Add(new SqlParameter("@Take", SqlDbType.Int) { Value = pageSize });

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var record = new RateRequestRecord
                        {
                            RequestId = reader.GetInt32(0),
                            UserName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Address = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            City = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            State = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Zip = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Country = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            RequestDate = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                        };

                        var responseJson = reader.IsDBNull(8) ? "" : reader.GetString(8);
                        var requestJson = reader.IsDBNull(9) ? "" : reader.GetString(9);
                        record.ParsedServices = ParseResponseJson(responseJson, record);
                        record.ParsedRequest = ParseRequestJson(requestJson);

                        var cityStateKey = (record.ParsedRequest.ShipFromCity ?? "").Trim().ToUpperInvariant()
                            + "|" + (record.ParsedRequest.ShipFromState ?? "").Trim().ToUpperInvariant();
                        string plantId;
                        if (!string.IsNullOrEmpty(cityStateKey.Replace("|", "")) &&
                            plantLookup.TryGetValue(cityStateKey, out plantId))
                        {
                            record.ParsedRequest.ShipFromPlantId = plantId;
                        }

                        records.Add(record);
                    }
                }

                ViewBag.CompareIds = MatchableRequestIds(conn, records.Select(r => r.RequestId));
            }

            return View(records);
        }

        /// <summary>
        /// Rows that get a Compare button. Past Quotes must keep working even if Database/004 hasn't
        /// been deployed yet, so any failure here just means no Compare buttons.
        /// </summary>
        private static HashSet<int> MatchableRequestIds(SqlConnection conn, IEnumerable<int> requestIds)
        {
            try
            {
                RateChangeData.CatchUp(conn);
                return RateChangeData.MatchableRequestIds(conn, requestIds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Compare lookup failed: " + ex.Message);
                return new HashSet<int>();
            }
        }

        // GET: Reports/Compare/5
        public ActionResult Compare(int id)
        {
            using (var conn = new SqlConnection(Configuration.UpsRateSqlConnection))
            {
                conn.Open();
                RateChangeData.CatchUp(conn);
                var group = RateChangeData.LoadCompare(conn, id);
                if (group == null) return HttpNotFound("Quote " + id + " can't be matched to other Quotes.");

                ViewBag.RequestId = id;
                ViewBag.Plant = Plant.Plants().FirstOrDefault(p => string.Equals(p.Id, group.Key.PlantCode, StringComparison.OrdinalIgnoreCase));
                return View(group);
            }
        }

        public const int RateChangesDefaultDays = 90;

        // GET: Reports/RateChanges
        public ActionResult RateChanges(string plant = null, DateTime? from = null, DateTime? to = null, bool changesOnly = true)
        {
            var plants = Plant.Plants();
            var toDate = (to ?? DateTime.Today).Date;
            var fromDate = (from ?? toDate.AddDays(-RateChangesDefaultDays)).Date;

            ViewBag.Plants = plants.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
            ViewBag.SelectedPlant = plant;
            ViewBag.From = fromDate;
            ViewBag.To = toDate;
            ViewBag.ChangesOnly = changesOnly;

            var groups = new List<MatchGroup>();
            var selectedPlant = plants.FirstOrDefault(p => string.Equals(p.Id, plant, StringComparison.OrdinalIgnoreCase));
            if (selectedPlant != null)
            {
                using (var conn = new SqlConnection(Configuration.UpsRateSqlConnection))
                {
                    conn.Open();
                    RateChangeData.CatchUp(conn);
                    groups = RateChangeData.LoadRateChanges(conn, selectedPlant.Id, fromDate, toDate.AddDays(1))
                        // At least two Quotes of some service, so every listed group has a row to show.
                        .Where(g => g.Services.Any(s => s.Entries.Count >= 2) && (!changesOnly || g.HasRateChanges))
                        .OrderByDescending(g => g.Quotes.Last().RequestDate)
                        .ToList();
                }
            }

            return View(groups);
        }

        private static List<ParsedRateService> ParseResponseJson(string json, RateRequestRecord record)
        {
            var result = new List<ParsedRateService>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var jObj = JObject.Parse(json);
                var ratedShipments = jObj.SelectToken("RateResponse.RatedShipment");
                if (ratedShipments == null) return result;

                var shipmentArray = ratedShipments.Type == JTokenType.Array
                    ? (JArray)ratedShipments
                    : new JArray(ratedShipments);

                foreach (var shipment in shipmentArray)
                {
                    var serviceCode = shipment.SelectToken("Service.Code")?.ToString() ?? "";
                    result.Add(new ParsedRateService
                    {
                        ServiceCode = serviceCode,
                        ServiceName = UpsServiceNames.For(serviceCode),
                        PublishedRate = shipment.SelectToken("TotalCharges.MonetaryValue")?.ToString() ?? "",
                        NegotiatedRate = shipment.SelectToken("NegotiatedRateCharges.TotalCharge.MonetaryValue")?.ToString() ?? "",
                        TransitDays = shipment.SelectToken("GuaranteedDelivery.BusinessDaysInTransit")?.ToString() ?? "-"
                    });
                }
            }
            catch (Exception ex)
            {
                record.ParseError = ex.Message;
            }

            return result;
        }

        private static ParsedRequestInfo ParseRequestJson(string json)
        {
            var info = new ParsedRequestInfo();
            if (string.IsNullOrWhiteSpace(json)) return info;

            try
            {
                var jObj = JObject.Parse(json);

                info.ShipFromName = jObj.SelectToken("RateRequest.Shipment.ShipFrom.Name")?.ToString() ?? "";
                info.ShipFromCity = jObj.SelectToken("RateRequest.Shipment.ShipFrom.Address.City")?.ToString() ?? "";
                info.ShipFromState = jObj.SelectToken("RateRequest.Shipment.ShipFrom.Address.StateProvinceCode")?.ToString() ?? "";

                info.ShipToCity = jObj.SelectToken("RateRequest.Shipment.ShipTo.Address.City")?.ToString() ?? "";
                info.ShipToState = jObj.SelectToken("RateRequest.Shipment.ShipTo.Address.StateProvinceCode")?.ToString() ?? "";
                info.ShipToZip = jObj.SelectToken("RateRequest.Shipment.ShipTo.Address.PostalCode")?.ToString() ?? "";
                info.ShipToCountry = jObj.SelectToken("RateRequest.Shipment.ShipTo.Address.CountryCode")?.ToString() ?? "";

                info.NumOfPieces = jObj.SelectToken("RateRequest.Shipment.NumOfPieces")?.ToString() ?? "";

                var packages = jObj.SelectToken("RateRequest.Shipment.Package");
                if (packages != null)
                {
                    var pkgArray = packages.Type == JTokenType.Array
                        ? (JArray)packages
                        : new JArray(packages);

                    foreach (var pkg in pkgArray)
                    {
                        var weight = pkg.SelectToken("PackageWeight.Weight")?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(weight))
                            info.PackageWeights.Add(weight);
                    }
                }
            }
            catch (Exception ex)
            {
                info.ParseError = ex.Message;
            }

            return info;
        }
    }
}