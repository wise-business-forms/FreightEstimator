using System;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// One shadow-mode comparison row: what a call site's existing (still-live) formula produced
    /// next to what the new CalculatedRate module produced for the same quote/service.
    /// See docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md.
    /// </summary>
    public class CalculatedRateShadowComparison
    {
        public Guid QuoteId { get; set; }
        public string CallSite { get; set; }
        public string PlantCode { get; set; }
        public string ServiceCode { get; set; }
        public string ServiceName { get; set; }
        public decimal ExistingCalculatedRate { get; set; }
        public decimal NewCalculatedRate { get; set; }
        public decimal Delta => NewCalculatedRate - ExistingCalculatedRate;
    }
}
