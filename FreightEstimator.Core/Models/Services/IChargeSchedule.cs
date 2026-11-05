namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// The seam CalculatedRate.Apply reads Plant Charges through, scoped to just the lookup it
    /// needs (see docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md). Two implementations:
    /// RateCalculationsChargeSchedule (production) and an in-memory fake used by tests.
    /// </summary>
    public interface IChargeSchedule
    {
        PlantServiceCharges GetCharges(string plantCode, string serviceName, bool isCwtEligible);
    }
}
