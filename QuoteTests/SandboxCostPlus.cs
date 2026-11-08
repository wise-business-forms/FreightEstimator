using System.Configuration;
using System.Data.SqlClient;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// CostPlus requests in the FreightEstimator test database, in CostPlus's own layout (dbo.FreightRequests), for the poll
    /// cycle tests (#31). Each request gets a unique LoginId, so tests never meet each other's rows, and Dispose removes the
    /// requests, their claims and the Quotes made for them. Makes the ALP plant quotable, and puts any plant it changes
    /// (see <see cref="SetPlantQuotable"/>) back as it found it. Refuses to run against anything but the test database
    /// (SandboxGuard).
    /// </summary>
    public sealed class SandboxCostPlus : IDisposable
    {
        private readonly List<string> _loginIds = new List<string>();
        private readonly Dictionary<string, (bool Existed, string Active, string UpsShippingNumber)> _plantsChanged =
            new Dictionary<string, (bool, string, string)>();

        public SandboxCostPlus()
        {
            SandboxGuard.Require();
            RequireSchema();
            SetPlantQuotable("ALP", true);
        }

        /// <summary>
        /// Makes a plant quotable (active, with a UPS shipping number) or not, adding the plant if the database has none. Dispose
        /// puts the plant back exactly as it was, and removes it if it was added here.
        /// </summary>
        public void SetPlantQuotable(string plantCode, bool quotable)
        {
            using (var conn = Open())
            {
                bool existed;
                string active = null, ship = null;
                using (var cmd = new SqlCommand("SELECT TOP 1 Active, UpsShippingNumber FROM dbo.Plants WHERE PlantCode = @p", conn))
                {
                    cmd.Parameters.AddWithValue("@p", plantCode);
                    using (var r = cmd.ExecuteReader())
                    {
                        existed = r.Read();
                        if (existed)
                        {
                            active = r.IsDBNull(0) ? null : r.GetString(0);
                            ship = r.IsDBNull(1) ? null : r.GetString(1);
                        }
                    }
                }
                if (!_plantsChanged.ContainsKey(plantCode)) _plantsChanged[plantCode] = (existed, active, ship);

                string sql = existed
                    ? "UPDATE dbo.Plants SET Active = @a, UpsShippingNumber = @s WHERE PlantCode = @p"
                    : "INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES (@p, @p + ' (test)', '1 Test Way', 'Testville', 'GA', '30004', 'US', @a, 0, @s)";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@p", plantCode);
                    cmd.Parameters.AddWithValue("@a", quotable ? "Y" : "N");
                    cmd.Parameters.AddWithValue("@s", quotable ? "TESTPLANT" : (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
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

        public string Add(Request r) => Add(r, "QT-" + Guid.NewGuid().ToString("N").Substring(0, 20));

        /// <summary>Adds a request for a LoginId that may already have one (a resubmission: same LoginId, QtyNumber 1, a new DateRequested).</summary>
        public string Add(Request r, string loginId)
        {
            if (!_loginIds.Contains(loginId)) _loginIds.Add(loginId);
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
                cmd.Parameters.AddWithValue("@fs", (object)r.FromState ?? DBNull.Value);
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
            public bool? ClassifiedResidential;
            public string SubstitutedZip;
            /// <summary>The filled result slots, in slot order.</summary>
            public List<Slot> Slots = new List<Slot>();
        }

        public class Slot
        {
            public string Carrier, Service, Note;
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
                        result.ClassifiedResidential = r["ClassifiedResidential"] as bool?;
                        result.SubstitutedZip = r["SubstitutedZip"] as string;
                        for (int i = 1; i <= 15; i++)
                        {
                            string n = i.ToString("00");
                            if (r["Carrier" + n] is DBNull && r["Service" + n] is DBNull && r["Rate" + n] is DBNull) continue;
                            result.Slots.Add(new Slot { Carrier = r["Carrier" + n] as string, Service = r["Service" + n] as string, Note = r["Note" + n] as string, Rate = r["Rate" + n] is DBNull ? 0m : (decimal)r["Rate" + n] });
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

        public int ClaimCount(string loginId) => Count("SELECT COUNT(*) FROM dbo.CostPlusRequestClaim WHERE LoginId = @p", loginId);

        /// <summary>How many Quotes were logged for this LoginId's requests (the Quote's request JSON carries the LoginId).</summary>
        public int QuoteCount(string loginId) =>
            Count("SELECT COUNT(*) FROM dbo.Quote q JOIN dbo.Consumer c ON c.ConsumerId = q.ConsumerId WHERE c.Name = 'CostPlus' AND q.RequestJson LIKE @p",
                "%\"LoginId\":\"" + loginId + "\"%");

        private static int Count(string sql, string parameter)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@p", parameter);
                return (int)cmd.ExecuteScalar();
            }
        }

        /// <summary>
        /// Puts an unfinished claim on the request as another poller instance would have: taken <paramref name="ageSeconds"/> ago
        /// by the database's clock. One older than the lease is a crashed instance's.
        /// </summary>
        public void AddClaim(string loginId, int ageSeconds)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand(
                "INSERT dbo.CostPlusRequestClaim (LoginId, QtyNumber, DateRequested, ClaimedBy, ClaimedAt, CompletedAt) " +
                "SELECT LoginId, QtyNumber, DateRequested, 'another-instance', DATEADD(SECOND, -@age, SYSDATETIME()), NULL " +
                "FROM dbo.FreightRequests WHERE LoginId = @l", conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                cmd.Parameters.AddWithValue("@age", ageSeconds);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Answers the request and finishes its claim as another poller instance would: one UPS slot at <paramref name="rate"/>,
        /// and the claim pointing at <paramref name="quoteId"/>. Needs the claim to exist already.
        /// </summary>
        public void AnswerAsAnotherInstance(string loginId, Guid quoteId, decimal rate)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand(
                "UPDATE dbo.FreightRequests SET Carrier01 = 'UPS', Service01 = 'OtherInstance', Rate01 = @r, DateProcessed = GETDATE(), DateRated = GETDATE() WHERE LoginId = @l; " +
                "UPDATE dbo.CostPlusRequestClaim SET CompletedAt = SYSDATETIME(), QuoteId = @q WHERE LoginId = @l;", conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                cmd.Parameters.AddWithValue("@r", rate);
                cmd.Parameters.AddWithValue("@q", quoteId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>How many of this LoginId's request rows have been answered (rated and processed).</summary>
        public int AnsweredRowCount(string loginId) =>
            Count("SELECT COUNT(*) FROM dbo.FreightRequests WHERE LoginId = @p AND DateRated IS NOT NULL AND DateProcessed IS NOT NULL", loginId);

        /// <summary>Leaves the request half finished, as an earlier writer might: rated, but never marked processed.</summary>
        public void MarkRatedButNotProcessed(string loginId)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("UPDATE dbo.FreightRequests SET DateRated = GETDATE() WHERE LoginId = @l", conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>The shadow run's comparison for the request (#36), or null if it made none.</summary>
        public ShadowRow ShadowOf(string loginId)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT Diverged, Differences, Explanation, Status, ConsoleResult, PollerResult FROM dbo.CostPlusShadowResult WHERE LoginId = @l", conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new ShadowRow
                    {
                        Diverged = r.GetBoolean(0), Differences = r.IsDBNull(1) ? null : r.GetString(1), Explanation = r.IsDBNull(2) ? null : r.GetString(2),
                        Status = r.GetString(3), ConsoleResult = r.GetString(4), PollerResult = r.GetString(5)
                    };
                }
            }
        }

        public class ShadowRow
        {
            public bool Diverged;
            public string Differences, Explanation, Status, ConsoleResult, PollerResult;
        }

        public void Explain(string loginId, string explanation) =>
            Execute("UPDATE dbo.CostPlusShadowResult SET Explanation = @p WHERE LoginId = @l", loginId, explanation);

        /// <summary>Changes what the console app "wrote" for the first service, so the poller's own result differs.</summary>
        public void ChangeConsoleRate(string loginId, decimal delta) =>
            Execute("UPDATE dbo.FreightRequests SET Rate01 = Rate01 + @p WHERE LoginId = @l", loginId, delta);

        /// <summary>Marks the request answered, as the console app would (no result slots).</summary>
        public void MarkAnswered(string loginId) =>
            Execute("UPDATE dbo.FreightRequests SET DateProcessed = GETDATE(), DateRated = GETDATE() WHERE LoginId = @l AND 1 = @p", loginId, 1);

        private static void Execute(string sql, string loginId, object parameter)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@l", loginId);
                cmd.Parameters.AddWithValue("@p", parameter);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Rows in a table, for "nothing was logged" checks (the test database is used by one test at a time).</summary>
        public static int RowCount(string table)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM " + table, conn))
                return (int)cmd.ExecuteScalar();
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
                        "DELETE FROM dbo.CostPlusShadowResult WHERE LoginId = @l; " +
                        "DELETE FROM dbo.CostPlusRequestClaim WHERE LoginId = @l; " +
                        "DELETE FROM dbo.FreightRequests WHERE LoginId = @l;", conn))
                    {
                        cmd.Parameters.AddWithValue("@l", loginId);
                        cmd.ExecuteNonQuery();
                    }
                }
                foreach (var changed in _plantsChanged)
                {
                    string sql = changed.Value.Existed
                        ? "UPDATE dbo.Plants SET Active = @a, UpsShippingNumber = @s WHERE PlantCode = @p"
                        : "DELETE FROM dbo.Plants WHERE PlantCode = @p";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@p", changed.Key);
                        if (changed.Value.Existed)
                        {
                            cmd.Parameters.AddWithValue("@a", (object)changed.Value.Active ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@s", (object)changed.Value.UpsShippingNumber ?? DBNull.Value);
                        }
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void RequireSchema()
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID('dbo.FreightRequests', 'U') IS NOT NULL AND OBJECT_ID('dbo.CostPlusRequestClaim', 'U') IS NOT NULL " +
                "AND OBJECT_ID('dbo.CostPlusReferenceNumber', 'SO') IS NOT NULL AND OBJECT_ID('dbo.CostPlusShadowResult', 'U') IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.Consumer WHERE Name = 'CostPlus') THEN 1 ELSE 0 END", conn))
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
