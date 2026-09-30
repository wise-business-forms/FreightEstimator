using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace AuthenticationServer.Models.Reports
{
    /// <summary>
    /// Reads Quote facts for the Compare and Rate Changes reports from the derived tables in
    /// Database/004 (RateRequestMatchKey, RateRequestServiceRate) plus QuoteServiceCalculation.
    /// Read-only apart from the derived-table catch-up. All matching and attribution lives in RateChanges.
    /// </summary>
    public static class RateChangeData
    {
        /// <summary>Brings the derived tables up to date with any new Requests_Rate rows.</summary>
        public static void CatchUp(SqlConnection conn)
        {
            using (var cmd = new SqlCommand("dbo.RecordRateRequestMatchKeys", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 })
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Which of these requests have a usable match key (and so get a Compare button).</summary>
        public static HashSet<int> MatchableRequestIds(SqlConnection conn, IEnumerable<int> requestIds)
        {
            var ids = requestIds.Distinct().ToList();
            var result = new HashSet<int>();
            if (ids.Count == 0) return result;

            // RequestIds are ints from our own query, so inlining them is safe.
            var sql = "SELECT RequestId FROM dbo.RateRequestMatchKey WHERE IsMatchable = 1 AND RequestId IN ("
                      + string.Join(",", ids) + ")";
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read()) result.Add(reader.GetInt32(0));
            }
            return result;
        }

        /// <summary>The Match Group the request belongs to, or null if the request can't be matched.</summary>
        public static MatchGroup LoadCompare(SqlConnection conn, int requestId)
        {
            const string selectGroup = @"
                SELECT k.RequestId, k.RequestDate, k.PlantCode, k.ShipToZip, k.PackageCount, k.WeightsKey, k.ResidentialIndicatorSent
                INTO #group
                FROM dbo.RateRequestMatchKey t
                JOIN dbo.RateRequestMatchKey k
                  ON k.IsMatchable = 1
                 AND k.PlantCode = t.PlantCode
                 AND k.ShipToZip = t.ShipToZip
                 AND k.PackageCount = t.PackageCount
                 AND k.WeightsKey = t.WeightsKey
                 AND k.ResidentialIndicatorSent = t.ResidentialIndicatorSent
                WHERE t.RequestId = @RequestId AND t.IsMatchable = 1;";

            var groups = Load(conn, selectGroup, c => c.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = requestId }));
            return groups.SingleOrDefault();
        }

        /// <summary>Every Match Group for the plant, built from Quotes requested in [from, toExclusive).</summary>
        public static List<MatchGroup> LoadRateChanges(SqlConnection conn, string plantCode, DateTime from, DateTime toExclusive)
        {
            const string selectGroup = @"
                SELECT k.RequestId, k.RequestDate, k.PlantCode, k.ShipToZip, k.PackageCount, k.WeightsKey, k.ResidentialIndicatorSent
                INTO #group
                FROM dbo.RateRequestMatchKey k
                WHERE k.IsMatchable = 1 AND k.PlantCode = @PlantCode
                  AND k.RequestDate >= @From AND k.RequestDate < @To;";

            return Load(conn, selectGroup, c =>
            {
                c.Parameters.Add(new SqlParameter("@PlantCode", SqlDbType.VarChar, 10) { Value = plantCode });
                c.Parameters.Add(new SqlParameter("@From", SqlDbType.DateTime) { Value = from });
                c.Parameters.Add(new SqlParameter("@To", SqlDbType.DateTime) { Value = toExclusive });
            });
        }

        /// <summary>
        /// selectGroup fills #group with the Quotes to group. This then loads the calculations that
        /// could belong to them and the other same-plant Quotes that could compete for those
        /// calculations, and hands all three to RateChanges.Build.
        /// </summary>
        private static List<MatchGroup> Load(SqlConnection conn, string selectGroup, Action<SqlCommand> addParameters)
        {
            var sql = selectGroup + @"
                SELECT c.QuoteServiceCalculationId, c.PlantCode, c.ServiceCode, c.CarrierRate, c.IsNegotiatedRate,
                       c.MarkupPercent, c.PerPackageCharge, c.PerShipmentCharge, c.CalculatedRate, c.CreatedDate
                INTO #calc
                FROM dbo.QuoteServiceCalculation c
                WHERE c.Carrier = 'UPS'
                  AND c.PlantCode IN (SELECT DISTINCT PlantCode FROM #group)
                  AND EXISTS (SELECT 1 FROM #group g
                              WHERE g.PlantCode = c.PlantCode
                                AND c.CreatedDate >= g.RequestDate
                                AND c.CreatedDate <= DATEADD(SECOND, @WindowSeconds, g.RequestDate));

                -- Any same-plant request, matchable or not, can compete for a calculation.
                SELECT k.RequestId, k.RequestDate, k.PlantCode, k.ShipToZip, k.PackageCount, k.WeightsKey, k.ResidentialIndicatorSent
                INTO #competing
                FROM dbo.RateRequestMatchKey k
                WHERE k.PlantCode IN (SELECT DISTINCT PlantCode FROM #calc)
                  AND k.RequestId NOT IN (SELECT RequestId FROM #group)
                  AND EXISTS (SELECT 1 FROM #calc c
                              WHERE c.PlantCode = k.PlantCode
                                AND k.RequestDate <= c.CreatedDate
                                AND k.RequestDate >= DATEADD(SECOND, -@WindowSeconds, c.CreatedDate));

                SELECT 1 AS IsGrouped, * FROM #group
                UNION ALL
                SELECT 0, * FROM #competing;

                SELECT s.RequestId, s.ServiceCode, s.PublishedRate, s.NegotiatedRate
                FROM dbo.RateRequestServiceRate s
                WHERE s.RequestId IN (SELECT RequestId FROM #group UNION SELECT RequestId FROM #competing);

                SELECT * FROM #calc;

                DROP TABLE #group; DROP TABLE #calc; DROP TABLE #competing;";

            var keyed = new List<KeyedRequest>();
            var ratesByRequest = new Dictionary<int, List<ServiceRate>>();
            var calculations = new List<CalculationCandidate>();

            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 })
            {
                addParameters(cmd);
                cmd.Parameters.Add(new SqlParameter("@WindowSeconds", SqlDbType.Int) { Value = (int)RateChanges.CalculationWindow.TotalSeconds });

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        keyed.Add(new KeyedRequest
                        {
                            IsGrouped = reader.GetInt32(0) == 1,
                            RequestId = reader.GetInt32(1),
                            RequestDate = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),
                            Key = new MatchKey(
                                reader.IsDBNull(3) ? null : reader.GetString(3),
                                reader.IsDBNull(4) ? null : reader.GetString(4),
                                reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                                reader.IsDBNull(6) ? null : reader.GetString(6),
                                reader.GetBoolean(7))
                        });
                    }

                    reader.NextResult();
                    while (reader.Read())
                    {
                        var requestId = reader.GetInt32(0);
                        if (!ratesByRequest.TryGetValue(requestId, out var rates))
                        {
                            rates = new List<ServiceRate>();
                            ratesByRequest[requestId] = rates;
                        }
                        var serviceCode = reader.GetString(1);
                        rates.Add(new ServiceRate(
                            serviceCode,
                            UpsServiceNames.For(serviceCode),
                            reader.IsDBNull(2) ? (decimal?)null : reader.GetDecimal(2),
                            reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3)));
                    }

                    reader.NextResult();
                    while (reader.Read())
                    {
                        calculations.Add(new CalculationCandidate
                        {
                            QuoteServiceCalculationId = reader.GetInt32(0),
                            PlantCode = reader.IsDBNull(1) ? null : reader.GetString(1),
                            ServiceCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                            CarrierRate = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
                            IsNegotiatedRate = reader.IsDBNull(4) ? (bool?)null : reader.GetBoolean(4),
                            MarkupPercent = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5),
                            PerPackageCharge = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
                            PerShipmentCharge = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7),
                            CalculatedRate = reader.GetDecimal(8),
                            CreatedDate = reader.GetDateTime(9)
                        });
                    }
                }
            }

            Func<KeyedRequest, QuoteFacts> toQuote = k =>
                new QuoteFacts(k.RequestId, k.RequestDate, k.Key,
                    ratesByRequest.TryGetValue(k.RequestId, out var rates) ? rates : new List<ServiceRate>());

            return RateChanges.Build(
                keyed.Where(k => k.IsGrouped).Select(toQuote),
                calculations,
                competingQuotes: keyed.Where(k => !k.IsGrouped).Select(toQuote));
        }

        /// <summary>A RateRequestMatchKey row before its rates are attached; IsGrouped false means a competing Quote.</summary>
        private sealed class KeyedRequest
        {
            public bool IsGrouped { get; set; }
            public int RequestId { get; set; }
            public DateTime RequestDate { get; set; }
            public MatchKey Key { get; set; }
        }
    }

    /// <summary>UPS service codes as the Past Quotes report names them.</summary>
    public static class UpsServiceNames
    {
        public static string For(string code)
        {
            switch (code)
            {
                case "01": return "UPS Next Day Air";
                case "02": return "UPS 2nd Day Air";
                case "03": return "UPS Ground";
                case "07": return "UPS Worldwide Express";
                case "08": return "UPS Worldwide Expedited";
                case "11": return "UPS Standard";
                case "12": return "UPS 3 Day Select";
                case "13": return "Next Day Air Saver";
                case "14": return "Next Day Air Early AM";
                case "54": return "Express Plus";
                case "59": return "2nd Day Air AM";
                case "65": return "UPS Saver";
                default: return string.Format("Service {0}", code);
            }
        }
    }
}
