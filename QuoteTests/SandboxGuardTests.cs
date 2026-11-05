namespace FreightEstimator.QuoteTests
{
    public class SandboxGuardTests
    {
        [Test]
        public void TheConfiguredUpsRateDatabaseIsSandbox()
        {
            // Passes only when ConnectionStrings.Sandbox.config names a non-production server and database UpsRate.
            SandboxGuard.Require();
        }
    }
}
