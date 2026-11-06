using System.Configuration;
using System.Data.SqlClient;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// CostPlus requests in the FreightEstimator test database, in CostPlus's own layout (dbo.FreightRequests), for the poll
    /// cycle tests (#31). Each request gets a unique LoginId, so tests never meet each other's rows, and Dispose removes the
    /// requests, their claims and the Quotes made for them. Adds the ALP plant (Georgia) if the database has none, and
    /// removes it again only if it was added here. Refuses to run against anything but the test database (SandboxGuard).
    /// </summary>
    public sealed class SandboxCostPlus : IDisposable
    {
        private readonly List<string> _loginIds = new List<string>();
        private readonly bool _addedPlant;

        public SandboxCostPlus()
        {
            SandboxGuard.Require();
            RequireSchema();

            using (var conn = Open())
            using (var cmd = new SqlCommand(
                "IF NOT EXISTS (SELECT 1 FROM dbo.Plants WHERE PlantCode = 'ALP') " +
                "BEGIN INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, UpsShippingNumber) " +
                "VALUES ('ALP', 'Alpharetta (test)', '1 Test Way', 'Alpharetta', 'GA', '30004', 'US', 'Y', 'TESTPLANT'); SELECT 1; END ELSE SELECT 0", conn))
            {
                _addedPlant = (int)cmd.ExecuteScalar() == 1;
            }
        }

        /// <summary>What a CostPlus request says. Defaults to a UPS-only request from Georgia.</summary>
        public class Request
        {
            public string FromState = "GA";
            public bool RequestUps = true;
            public bool? RequestLtl = false;
            public string ToAddress = "1 Main St", ToCity = "Anytown", ToState, ToZip, ToCountry = "US";
            public int NumPackages, PkgWeight, LastPkgWeight;
            public DateTime DateRequested = DateTime.Now.AddSeconds(-5);
        }

        /// <summary>A request for the shipment in a recorded UPS case (the recorded answer is what the carrier fake returns).</summary>
        public static Request For(QuoteCase c) => new Request
        {
            ToState = c.ShipToState, ToZip = c.ShipToZip,
            NumPackages = c.Packages, PkgWeight = (int)c.PackageWeight, LastPkgWeight = (int)c.LastPackageWeight
        };

        public string Add(Request r)
        {
            string loginId = "QT-" + Guid.NewGuid().ToString("N").Substring(0, 20);
            _loginIds.Add(loginId);
            using (var conn = Open())
            using (var cmd = new SqlCommand(
                "INSERT dbo.FreightRequests (LoginId, QtyNumber, ToAddress, ToCity, ToState, ToZip, ToCountry, FromAddress, FromCity, FromState, FromZip, FromCountry, " +
                "NumPackages, PkgWeight, LastPkgWeight, RequestUPS, RequestLTL, DateRequested) " +
                "VALUES (@l, 1, @ta, @tc, @ts, @tz, @tco, '1 Test Way', 'Alpharetta', @fs, '30004', 'US', @n, @pw, @lw, @u, @ltl, @d)", conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                cmd.Parameters.AddWithValue("@ta", r.ToAddress);
                cmd.Parameters.AddWithValue("@tc", r.ToCity);
                cmd.Parameters.AddWithValue("@ts", r.ToState);
                cmd.Parameters.AddWithValue("@tz", r.ToZip);
                cmd.Parameters.AddWithValue("@tco", r.ToCountry);
                cmd.Parameters.AddWithValue("@fs", r.FromState);
                cmd.Parameters.AddWithValue("@n", r.NumPackages);
                cmd.Parameters.AddWithValue("@pw", r.PkgWeight);
                cmd.Parameters.AddWithValue("@lw", r.LastPkgWeight);
                cmd.Parameters.AddWithValue("@u", r.RequestUps);
                cmd.Parameters.AddWithValue("@ltl", (object)r.RequestLtl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@d", r.DateRequested);
                cmd.ExecuteNonQuery();
            }
            return loginId;
        }

        /// <summary>What FileMaker would read back from the request's row.</summary>
        public class Result
        {
            public DateTime? DateProcessed, DateRated;
            public string ErrorCode01, ErrorDesc01;
            /// <summary>The filled result slots, in slot order.</summary>
            public List<Slot> Slots = new List<Slot>();
        }

        public class Slot
        {
            public string Carrier, Service;
            public decimal Rate;
        }

        public Result ResultOf(string loginId)
        {
            var result = new Result();
            using (var conn = Open())
            {
                using (var cmd = new SqlCommand("SELECT * FROM dbo.FreightRequests WHERE LoginId = @l", conn))
                {
                    cmd.Parameters.AddWithValue("@l", loginId);
                    using (var r = cmd.ExecuteReader())
                    {
                        Assert.That(r.Read(), Is.True, "the request row is gone");
                        result.DateProcessed = r["DateProcessed"] as DateTime?;
                        result.DateRated = r["DateRated"] as DateTime?;
                        result.ErrorCode01 = r["ErrorCode01"] as string;
                        result.ErrorDesc01 = r["ErrorDesc01"] as string;
                        for (int i = 1; i <= 15; i++)
                        {
                            string n = i.ToString("00");
                            if (r["Carrier" + n] is DBNull && r["Service" + n] is DBNull && r["Rate" + n] is DBNull) continue;
                            result.Slots.Add(new Slot { Carrier = r["Carrier" + n] as string, Service = r["Service" + n] as string, Rate = r["Rate" + n] is DBNull ? 0m : (decimal)r["Rate" + n] });
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>The claim a poll cycle took on the request, or null if it took none.</summary>
        public Claim ClaimOf(string loginId)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT ClaimedAt, CompletedAt, QuoteId FROM dbo.CostPlusRequestClaim WHERE LoginId = @l", conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new Claim { ClaimedAt = r.GetDateTime(0), CompletedAt = r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1), QuoteId = r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2) };
                }
            }
        }

        public class Claim
        {
            public DateTime ClaimedAt;
            public DateTime? CompletedAt;
            public Guid? QuoteId;
        }

        /// <summary>The Quote row for a Quote ID, with the name of the Consumer that asked, or null.</summary>
        public QuoteRow QuoteOf(Guid quoteId)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT c.Name, q.ReferenceNumber, q.PlantCode FROM dbo.Quote q JOIN dbo.Consumer c ON c.ConsumerId = q.ConsumerId WHERE q.QuoteId = @q", conn))
            {
                cmd.Parameters.AddWithValue("@q", quoteId);
                using (var r = cmd.ExecuteReader())
                {
                    return r.Read() ? new QuoteRow { Consumer = r.GetString(0), ReferenceNumber = r.GetString(1), PlantCode = r.GetString(2) } : null;
                }
            }
        }

        public class QuoteRow
        {
            public string Consumer, ReferenceNumber, PlantCode;
        }

        public void Dispose()
        {
            using (var conn = Open())
            {
                foreach (string loginId in _loginIds)
                {
                    using (var cmd = new SqlCommand(
                        "DECLARE @q TABLE (QuoteId UNIQUEIDENTIFIER); " +
                        "INSERT @q SELECT QuoteId FROM dbo.CostPlusRequestClaim WHERE LoginId = @l AND QuoteId IS NOT NULL; " +
                        "DELETE FROM dbo.QuoteServiceCalculation WHERE QuoteId IN (SELECT QuoteId FROM @q); " +
                        "DELETE FROM dbo.Quote WHERE QuoteId IN (SELECT QuoteId FROM @q); " +
                        "DELETE FROM dbo.CostPlusRequestClaim WHERE LoginId = @l; " +
                        "DELETE FROM dbo.FreightRequests WHERE LoginId = @l;", conn))
                    {
                        cmd.Parameters.AddWithValue("@l", loginId);
                        cmd.ExecuteNonQuery();
                    }
                }
                if (_addedPlant)
                    using (var cmd = new SqlCommand("DELETE FROM dbo.Plants WHERE PlantCode = 'ALP' AND UpsShippingNumber = 'TESTPLANT'", conn))
                        cmd.ExecuteNonQuery();
            }
        }

        private static void RequireSchema()
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID('dbo.FreightRequests', 'U') IS NOT NULL AND OBJECT_ID('dbo.CostPlusRequestClaim', 'U') IS NOT NULL " +
                "AND OBJECT_ID('dbo.CostPlusReferenceNumber', 'SO') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.Consumer WHERE Name = 'CostPlus') THEN 1 ELSE 0 END", conn))
            {
                if ((int)cmd.ExecuteScalar() != 1)
                    Assert.Fail("The FreightEstimator test database has no CostPlus objects yet. Run Database/FreightEstimator.sql against it.");
            }
        }

        private static SqlConnection Open()
        {
            var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["UpsRate"].ConnectionString);
            conn.Open();
            return conn;
        }
    }
}
