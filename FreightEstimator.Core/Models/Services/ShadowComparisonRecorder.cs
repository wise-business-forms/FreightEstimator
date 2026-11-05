using System;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// Shared shape for the shadow-mode comparisons made at each of the four call sites in
    /// docs/issues/0001-ups-markup-divergence.md: compute what CalculatedRate.Apply would have
    /// produced and log it next to what the call site's existing, still-live formula actually
    /// produced. See docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md.
    ///
    /// A failure here must never break the live quote flow, so building the RatingContext and
    /// calling Apply run inside the same try/catch as the write.
    /// </summary>
    public static class ShadowComparisonRecorder
    {
        public static void Record(
            string callSite,
            Guid quoteId,
            string plantCode,
            string chargeScheduleServiceName,
            string displayServiceName,
            int packageCount,
            decimal carrierRate,
            bool isCwtEligible,
            decimal existingCalculatedRate)
        {
            try
            {
                RatingContext context = new RatingContext
                {
                    PlantCode = plantCode,
                    ServiceName = chargeScheduleServiceName,
                    PackageCount = packageCount,
                    IsCwtEligible = isCwtEligible
                };

                CalculatedRate newCalculatedRate = CalculatedRate.Apply(carrierRate, new RateCalculationsChargeSchedule(), context);

                CalculatedRateShadowLog.Record(new CalculatedRateShadowComparison
                {
                    QuoteId = quoteId,
                    CallSite = callSite,
                    PlantCode = plantCode,
                    ServiceName = displayServiceName,
                    ExistingCalculatedRate = existingCalculatedRate,
                    NewCalculatedRate = newCalculatedRate.Amount
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"ShadowComparisonRecorder.Record failed for {callSite}: " + ex.Message);
            }
        }
    }
}
