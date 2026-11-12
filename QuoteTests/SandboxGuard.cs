using System.Configuration;
using System.Data.SqlClient;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// These tests write Plant Charges and quote logs. They must only ever do that to the FreightEstimator test
    /// database (which replaced the SANDBOX copy of UpsRate), never to UpsRate, so every fixture asks this guard first
    /// and the run aborts unless the configured connection is positively that: the database must be FreightEstimator. A deny-list of
    /// production names would pass any alias or IP address nobody thought to list. See docs/issues (#23): Debug builds default to ConnectionStrings.Sandbox.config.
    /// </summary>
    public static class SandboxGuard
    {
        public static void Require()
        {
            ConnectionStringSettings setting = ConfigurationManager.ConnectionStrings["FreightEstimator"];
            if (setting == null || string.IsNullOrWhiteSpace(setting.ConnectionString))
                Assert.Fail("No FreightEstimator connection string: create ConnectionStrings.Sandbox.config (gitignored) with a FreightEstimator entry for the FreightEstimator test database.");

            var builder = new SqlConnectionStringBuilder(setting.ConnectionString);

            if (!string.Equals(builder.InitialCatalog, "FreightEstimator", StringComparison.OrdinalIgnoreCase))
                Assert.Fail("Refusing to run: expected database FreightEstimator, found " + builder.InitialCatalog + ". UpsRate is never a test target.");
        }

        /// <summary>
        /// The Quote Log reads production UPSRate through its own UpsRateReadOnly setting (ADR 0007). Tests point that setting at the
        /// FreightEstimator test database and must never at UPSRate: this refuses a connection string whose database is UPSRate.
        /// </summary>
        public static void RequireNotUpsRate(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.Equals(builder.InitialCatalog, "UPSRate", StringComparison.OrdinalIgnoreCase))
                Assert.Fail("Refusing to run: the Quote Log connection names database UPSRate. UpsRate is never a test target.");
        }
    }
}
