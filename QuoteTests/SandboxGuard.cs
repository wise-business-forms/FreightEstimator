using System.Configuration;
using System.Data.SqlClient;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// These tests write Plant Charges and quote logs. They must only ever do that to the FreightEstimator test
    /// database (which replaced the SANDBOX copy of UpsRate), never to UpsRate, so every fixture asks this guard first
    /// and the run aborts unless the configured connection is positively that: the server name must contain "sandbox"
    /// (or be named in QUOTETESTS_SANDBOX_SERVER, e.g. AZUREDB01\AZUREDB01) and the database must be FreightEstimator. A deny-list of production names would pass any alias or IP address
    /// nobody thought to list. See docs/issues (#23): Debug builds default to ConnectionStrings.Sandbox.config.
    /// </summary>
    public static class SandboxGuard
    {
        public static void Require()
        {
            ConnectionStringSettings setting = ConfigurationManager.ConnectionStrings["UpsRate"];
            if (setting == null || string.IsNullOrWhiteSpace(setting.ConnectionString))
                Assert.Fail("No UpsRate connection string: create ConnectionStrings.Sandbox.config (gitignored) with an UpsRate entry for the FreightEstimator test database.");

            var builder = new SqlConnectionStringBuilder(setting.ConnectionString);

            string allowed = Environment.GetEnvironmentVariable("QUOTETESTS_SANDBOX_SERVER");
            bool isSandbox = builder.DataSource.IndexOf("sandbox", StringComparison.OrdinalIgnoreCase) >= 0
                          || (!string.IsNullOrEmpty(allowed) && string.Equals(builder.DataSource, allowed, StringComparison.OrdinalIgnoreCase));
            if (!isSandbox || builder.DataSource.StartsWith("SANDBOX_SERVER", StringComparison.OrdinalIgnoreCase))
                Assert.Fail("Refusing to run: UpsRate server '" + builder.DataSource + "' is not recognisably SANDBOX. "
                    + "Name it so (it must contain 'sandbox'), or set QUOTETESTS_SANDBOX_SERVER to its exact name, and make sure ConnectionStrings.Sandbox.config is not a template.");

            if (!string.Equals(builder.InitialCatalog, "FreightEstimator", StringComparison.OrdinalIgnoreCase))
                Assert.Fail("Refusing to run: expected database FreightEstimator, found " + builder.InitialCatalog + ". UpsRate is never a test target.");
        }
    }
}
