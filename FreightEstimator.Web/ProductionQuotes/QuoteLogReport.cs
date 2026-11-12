using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuthenticationServer.ProductionQuotes
{
    /// <summary>
    /// The Quote Log (#53, ADR 0007): the Quotes production has answered, read from production UPSRate through the read-only
    /// UpsRateReadOnly connection. It only ever reads: every statement goes through <see cref="Select"/>, which refuses anything
    /// that is not a plain SELECT. It shows no street address and no raw JSON.
    /// </summary>
    internal static class QuoteLogReport
    {
        public const int PageSize = 50;
        public const int DefaultDays = 7;

        public class Row
        {
            public int RequestId;
            public DateTime When;
            public string RequestedBy;
            public string ShipTo;
            public int Services;
            public string Outcome;
        }

        public class Page
        {
            public List<Row> Rows = new List<Row>();
            public int Total;
            public int Number;
            public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
        }

        // One word that writes, or a second statement, makes a command not a plain SELECT.
        private static readonly Regex NotReadOnly = new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|INTO|GRANT)\b|;", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The only way this report runs SQL: a command for a plain SELECT, or an exception.</summary>
        private static SqlCommand Select(SqlConnection conn, string sql)
        {
            if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || NotReadOnly.IsMatch(sql))
                throw new InvalidOperationException("The Quote Log reads production and runs plain SELECTs only.");
            return new SqlCommand(sql, conn);
        }

        /// <summary>One page of the UPS Quotes production answered from <paramref name="from"/> to the end of <paramref name="to"/>, newest first.</summary>
        public static Page Load(SqlConnection conn, DateTime from, DateTime to, int page)
        {
            DateTime start = from.Date, end = to.Date.AddDays(1);
            var result = new Page();

            using (SqlCommand count = Select(conn, "SELECT COUNT(*) FROM dbo.Requests_Rate WHERE RequestDate >= @from AND RequestDate < @to"))
            {
                count.Parameters.Add(new SqlParameter("@from", SqlDbType.DateTime) { Value = start });
                count.Parameters.Add(new SqlParameter("@to", SqlDbType.DateTime) { Value = end });
                result.Total = (int)count.ExecuteScalar();
            }

            // A page past the end (or a huge one) shows the last page, not an empty list or an overflowing offset.
            result.Number = Math.Min(Math.Max(1, page), Math.Max(1, result.TotalPages));

            // Only this page's rows leave the database with their JSON: the log tables have no indexes and may not be given any.
            using (SqlCommand cmd = Select(conn,
                @"SELECT RequestId, RequestDate, UserName, RequestXml, ResponseXml
                  FROM dbo.Requests_Rate
                  WHERE RequestDate >= @from AND RequestDate < @to
                  ORDER BY RequestDate DESC, RequestId DESC
                  OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY"))
            {
                cmd.Parameters.Add(new SqlParameter("@from", SqlDbType.DateTime) { Value = start });
                cmd.Parameters.Add(new SqlParameter("@to", SqlDbType.DateTime) { Value = end });
                cmd.Parameters.Add(new SqlParameter("@skip", SqlDbType.Int) { Value = (result.Number - 1) * PageSize });
                cmd.Parameters.Add(new SqlParameter("@take", SqlDbType.Int) { Value = PageSize });
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int services;
                        string outcome = OutcomeOf(reader.IsDBNull(4) ? null : reader.GetString(4), out services);
                        result.Rows.Add(new Row
                        {
                            RequestId = reader.GetInt32(0),
                            When = reader.GetDateTime(1),
                            RequestedBy = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            ShipTo = ShipToOf(reader.IsDBNull(3) ? null : reader.GetString(3)),
                            Services = services,
                            Outcome = outcome
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>The ship-to state and ZIP from the request JSON ("TN 37201"); blank when the request cannot be read.</summary>
        internal static string ShipToOf(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson)) return "";
            try
            {
                JObject request = JObject.Parse(requestJson);
                string state = request.SelectToken("RateRequest.Shipment.ShipTo.Address.StateProvinceCode")?.ToString() ?? "";
                string zip = request.SelectToken("RateRequest.Shipment.ShipTo.Address.PostalCode")?.ToString() ?? "";
                return (state + " " + zip).Trim();
            }
            catch (JsonException)
            {
                return "";
            }
        }

        /// <summary>
        /// Rated (UPS returned at least one service), Empty (none, or no response stored), or Error (UPS said why not, or the stored
        /// response cannot be read). <paramref name="services"/> is how many services UPS returned.
        /// </summary>
        internal static string OutcomeOf(string responseJson, out int services)
        {
            services = 0;
            if (string.IsNullOrWhiteSpace(responseJson)) return "Empty";
            try
            {
                JObject response = JObject.Parse(responseJson);

                if (response.SelectToken("response.errors") is JArray errors && errors.Count > 0)
                {
                    string code = errors[0].SelectToken("code")?.ToString();
                    string message = errors[0].SelectToken("message")?.ToString();
                    return ("Error " + code + ": " + message).Replace("  ", " ").TrimEnd(' ', ':');
                }

                JToken rated = response.SelectToken("RateResponse.RatedShipment");
                services = rated == null ? 0 : rated.Type == JTokenType.Array ? ((JArray)rated).Count : 1;
                return services > 0 ? "Rated" : "Empty";
            }
            catch (JsonException)
            {
                return "Error: response unreadable";
            }
        }
    }
}
