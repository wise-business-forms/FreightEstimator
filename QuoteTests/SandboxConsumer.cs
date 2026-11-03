using System.Configuration;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// A Consumer with a known API key in SANDBOX, for the Quote API tests. Needs the Consumer and Quote tables
    /// (Database/005_create_consumer_and_quote.sql, the script production gets) to exist already, then adds
    /// one Consumer. Dispose removes the Consumer and every Quote it made, so a run leaves SANDBOX as it found it.
    /// Refuses to run against anything but SANDBOX (SandboxGuard).
    /// </summary>
    public sealed class SandboxConsumer : IDisposable
    {
        public string Name { get; }
        public string ApiKey { get; }
        public int ConsumerId { get; }

        public SandboxConsumer(bool isActive = true)
        {
            SandboxGuard.Require();
            RequireSchema();

            Name = "QuoteTests-" + Guid.NewGuid().ToString("N");
            ApiKey = "key-" + Guid.NewGuid().ToString("N");

            using (var conn = Open())
            using (var cmd = new SqlCommand("INSERT INTO dbo.Consumer (Name, ApiKeyHash, IsActive) OUTPUT INSERTED.ConsumerId VALUES (@n, @h, @a)", conn))
            {
                cmd.Parameters.AddWithValue("@n", Name);
                cmd.Parameters.Add("@h", System.Data.SqlDbType.VarBinary, 32).Value = Hash(ApiKey);
                cmd.Parameters.AddWithValue("@a", isActive);
                ConsumerId = (int)cmd.ExecuteScalar();
            }
        }

        /// <summary>The Quote rows this Consumer's calls produced.</summary>
        public List<QuoteRow> Quotes()
        {
            var rows = new List<QuoteRow>();
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT QuoteId, ConsumerId, ReferenceNumber, PlantCode, RequestJson FROM dbo.Quote WHERE ConsumerId = @c ORDER BY CreatedDate, QuoteId", conn))
            {
                cmd.Parameters.AddWithValue("@c", ConsumerId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        rows.Add(new QuoteRow { QuoteId = r.GetGuid(0), ConsumerId = r.GetInt32(1), ReferenceNumber = r.GetString(2), PlantCode = r.GetString(3), RequestJson = r.GetString(4) });
                }
            }
            return rows;
        }

        public class QuoteRow
        {
            public Guid QuoteId;
            public int ConsumerId;
            public string ReferenceNumber, PlantCode, RequestJson;
        }

        public void Dispose()
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("DELETE FROM dbo.Quote WHERE ConsumerId = @c; DELETE FROM dbo.Consumer WHERE ConsumerId = @c", conn))
            {
                cmd.Parameters.AddWithValue("@c", ConsumerId);
                cmd.ExecuteNonQuery();
            }
        }

        private static byte[] Hash(string key)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        }

        /// <summary>
        /// The tests' login cannot create tables (schema scripts are run by whoever has DDL rights, like the
        /// others in Database/), so check the objects are there and say what to run if they are not.
        /// </summary>
        private static void RequireSchema()
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.Consumer', 'U') IS NOT NULL AND OBJECT_ID('dbo.Quote', 'U') IS NOT NULL AND OBJECT_ID('dbo.LogQuote', 'P') IS NOT NULL THEN 1 ELSE 0 END", conn))
            {
                if ((int)cmd.ExecuteScalar() != 1)
                    Assert.Fail("SANDBOX has no Consumer/Quote tables yet. Run Database/005_create_consumer_and_quote.sql against the SANDBOX UpsRate database (it needs a login that can create tables), then run the tests again.");
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
