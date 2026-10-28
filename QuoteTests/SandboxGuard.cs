using System.Configuration;
using System.Data.SqlClient;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// These tests write Plant Charges and quote logs. They must only ever do that to a SANDBOX copy of
    /// UpsRate, so every fixture asks this guard first and the run aborts if the configured connection
    /// could be production. See docs/issues (#23): Debug builds default to ConnectionStrings.Sandbox.config.
    /// </summary>
    public static class SandboxGuard
    {
        // The production SQL instance the app hardcoded before #23.
        private const string ProductionServer = @"AZUREDB01\AZUREDB01";

        public static void Require()
        {
            ConnectionStringSettings setting = ConfigurationManager.ConnectionStrings["UpsRate"];
            if (setting == null || string.IsNullOrWhiteSpace(setting.ConnectionString))
                Assert.Fail("No UpsRate connection string: copy ConnectionStrings.Sandbox.config.example to ConnectionStrings.Sandbox.config and fill it in.");

            var builder = new SqlConnectionStringBuilder(setting.ConnectionString);

            if (string.Equals(builder.DataSource, ProductionServer, StringComparison.OrdinalIgnoreCase))
                Assert.Fail("Refusing to run: UpsRate points at the production server (" + ProductionServer + ").");

            if (builder.DataSource.StartsWith("SANDBOX_SERVER", StringComparison.OrdinalIgnoreCase) || builder.DataSource == "USER")
                Assert.Fail("ConnectionStrings.Sandbox.config still holds the template placeholders.");

            if (!string.Equals(builder.InitialCatalog, "UpsRate", StringComparison.OrdinalIgnoreCase))
                Assert.Fail("Refusing to run: expected database UpsRate, found " + builder.InitialCatalog + ".");

            string productionFile = Path.Combine(AppContext.BaseDirectory, "ConnectionStrings.Production.config");
            if (File.Exists(productionFile) && File.ReadAllText(productionFile).Contains(builder.DataSource))
                Assert.Fail("Refusing to run: UpsRate points at the same server as ConnectionStrings.Production.config.");
        }
    }
}
