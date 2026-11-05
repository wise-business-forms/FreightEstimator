using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests.Replay
{
    /// <summary>One stored UPS rate call (dbo.Requests_Rate), reduced to what replaying it needs.</summary>
    public class StoredUpsResponse
    {
        public int RequestId;
        public string Plant;
        /// <summary>True for a Ground Freight (GRID 2) call, false for the parcel "Shop" call (GRID 1).</summary>
        public bool GroundFreight;
        public string Address, City, State, Zip, Country;
        public List<float> Weights;
        public string ResponseJson;
        public string Source => "Requests_Rate " + RequestId;
    }

    /// <summary>One stored Logistics Partner call (dbo.Log_LTL), reduced to what replaying it needs.</summary>
    public class StoredLtlResponse
    {
        public int RequestId;
        public string Plant;
        public string City, State, Zip, Country;
        public List<float> Weights;
        public int FreightClass;
        public DateTime PickupDate;
        public string ResponseXml;
        public string Source => "Log_LTL " + RequestId;
    }

    /// <summary>
    /// Reads stored carrier calls out of the SANDBOX UpsRate database. The copy of production rows into SANDBOX
    /// is a human step (customer addresses): this only reads what is there. Refuses to run on anything but SANDBOX.
    /// A row that cannot be read as a replayable call is returned in <see cref="Skipped"/> with the reason.
    /// </summary>
    public class StoredResponses
    {
        public List<StoredUpsResponse> Ups { get; } = new List<StoredUpsResponse>();
        public List<StoredLtlResponse> Ltl { get; } = new List<StoredLtlResponse>();
        public List<string> Skipped { get; } = new List<string>();

        /// <param name="newest">At most this many stored calls of each kind, newest first.</param>
        /// <param name="onlyUpsRequestIds">Restricts the UPS calls to these Requests_Rate ids (for tests); null reads by <paramref name="newest"/>.</param>
        /// <param name="onlyLtlRequestIds">Restricts the Logistics Partner calls to these Log_LTL ids; null reads by <paramref name="newest"/>.</param>
        public static StoredResponses Read(int newest, IReadOnlyCollection<int> onlyUpsRequestIds = null, IReadOnlyCollection<int> onlyLtlRequestIds = null)
        {
            SandboxGuard.Require();
            var result = new StoredResponses();
            using (var conn = Open())
            {
                List<(string Plant, string City, string State)> plants = ReadPlants(conn);
                result.ReadUps(conn, newest, onlyUpsRequestIds, plants);
                result.ReadLtl(conn, newest, onlyLtlRequestIds);
            }
            return result;
        }

        private static List<(string, string, string)> ReadPlants(SqlConnection conn)
        {
            var plants = new List<(string, string, string)>();
            using (var cmd = new SqlCommand("SELECT PlantCode, City, State FROM dbo.Plants WHERE Active = 'Y'", conn))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) plants.Add((r.GetString(0).Trim(), r.GetString(1).Trim(), r.GetString(2).Trim()));
            }
            return plants;
        }

        private void ReadUps(SqlConnection conn, int newest, IReadOnlyCollection<int> only, List<(string Plant, string City, string State)> plants)
        {
            string filter = only == null ? "" : only.Count == 0 ? " AND 1 = 0" : " AND RequestId IN (" + string.Join(",", only.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")";
            // Only calls UPS answered with rates: an error is logged in the same table, with the exception text as its response.
            string sql = "SELECT TOP (@n) RequestId, RequestXml, ResponseXml FROM dbo.Requests_Rate WHERE RequestXml LIKE '%RateRequest%' AND ResponseXml LIKE '%RatedShipment%'" + filter + " ORDER BY RequestId DESC";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@n", newest);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int id = r.GetInt32(0);
                        try { Ups.Add(ParseUps(id, r.GetString(1), r.GetString(2), plants)); }
                        catch (Exception ex) { Skipped.Add("Requests_Rate " + id + ": " + ex.Message); }
                    }
                }
            }
        }

        private void ReadLtl(SqlConnection conn, int newest, IReadOnlyCollection<int> only)
        {
            string filter = only == null ? "" : only.Count == 0 ? " AND 1 = 0" : " AND LTLRequestId IN (" + string.Join(",", only.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ")";
            string sql = "SELECT TOP (@n) LTLRequestId, PlantCode, FullRequest, XmlResponse FROM dbo.Log_LTL WHERE FullRequest LIKE '%RateRequest%' AND XmlResponse LIKE '%<data>%'" + filter + " ORDER BY LTLRequestId DESC";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@n", newest);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int id = r.GetInt32(0);
                        try { Ltl.Add(ParseLtl(id, r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetString(3))); }
                        catch (Exception ex) { Skipped.Add("Log_LTL " + id + ": " + ex.Message); }
                    }
                }
            }
        }

        public static StoredUpsResponse ParseUps(int id, string requestJson, string responseJson, List<(string Plant, string City, string State)> plants)
        {
            JToken shipment = JObject.Parse(requestJson)["RateRequest"]?["Shipment"] ?? throw new FormatException("no RateRequest.Shipment");
            JToken from = shipment["ShipFrom"]?["Address"] ?? throw new FormatException("no ShipFrom");
            JToken to = shipment["ShipTo"]?["Address"] ?? throw new FormatException("no ShipTo");

            string fromCity = ((string)from["City"] ?? "").Trim();
            string fromState = ((string)from["StateProvinceCode"] ?? "").Trim();
            string plant = plants.Where(p => string.Equals(p.City, fromCity, StringComparison.OrdinalIgnoreCase) && string.Equals(p.State, fromState, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Plant).FirstOrDefault();
            if (plant == null) throw new FormatException("ship-from " + fromCity + ", " + fromState + " is not an active plant");

            var weights = new List<float>();
            JToken packages = shipment["Package"];
            foreach (JToken p in packages is JArray array ? array : new JArray(packages))
            {
                weights.Add(float.Parse((string)p["PackageWeight"]["Weight"], CultureInfo.InvariantCulture));
            }
            if (weights.Count == 0) throw new FormatException("no packages");

            return new StoredUpsResponse
            {
                RequestId = id,
                Plant = plant,
                // Ground Freight is the only call that asks UPS for FRS payment information (see UPSRequest.RateRequest).
                GroundFreight = shipment["FRSPaymentInformation"] != null,
                Address = to["AddressLine"] is JArray lines && lines.Count > 0 ? (string)lines[0] : "",
                City = (string)to["City"],
                State = (string)to["StateProvinceCode"],
                Zip = (string)to["PostalCode"],
                Country = (string)to["CountryCode"],
                Weights = weights,
                ResponseJson = responseJson
            };
        }

        public static StoredLtlResponse ParseLtl(int id, string plant, string fullRequest, string xmlResponse)
        {
            // FullRequest is the form post: userid=..&password=..&request=<xml>. Only the xml is read, never the credentials.
            int start = fullRequest.IndexOf("&request=", StringComparison.Ordinal);
            if (start < 0) throw new FormatException("no request");
            XDocument request = XDocument.Parse(fullRequest.Substring(start + "&request=".Length));

            List<XElement> items = request.Descendants("Item").ToList();
            if (items.Count == 0) throw new FormatException("no items");
            XElement drop = request.Descendants("Event").FirstOrDefault(e => (string)e.Attribute("type") == "Drop") ?? throw new FormatException("no drop");
            XElement location = drop.Element("Location");

            return new StoredLtlResponse
            {
                RequestId = id,
                Plant = plant?.Trim(),
                City = (string)location.Element("City"),
                State = (string)location.Element("State"),
                Zip = (string)location.Element("Zip"),
                Country = (string)location.Element("Country"),
                Weights = items.Select(i => float.Parse((string)i.Element("Weight"), CultureInfo.InvariantCulture)).ToList(),
                FreightClass = int.Parse((string)items[0].Attribute("freightClass"), CultureInfo.InvariantCulture),
                PickupDate = DateTime.ParseExact((string)drop.Attribute("date"), "MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture),
                ResponseXml = xmlResponse
            };
        }

        private static SqlConnection Open()
        {
            var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["UpsRate"].ConnectionString);
            conn.Open();
            return conn;
        }
    }
}
