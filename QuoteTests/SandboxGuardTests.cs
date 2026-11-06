namespace FreightEstimator.QuoteTests
{
    public class SandboxGuardTests
    {
        [Test]
        public void TheConfiguredDatabaseIsTheFreightEstimatorTestDatabase()
        {
            // Passes only when ConnectionStrings.Sandbox.config names the test server and database FreightEstimator.
            SandboxGuard.Require();
        }
    }
}
