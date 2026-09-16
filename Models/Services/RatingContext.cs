namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// The minimal, immutable set of facts CalculatedRate.Apply needs beyond the Carrier Rate
    /// and Plant Charges. Deliberately not named ShipmentProfile or anything else that reads as
    /// a variant of Shipment (see CONTEXT.md: Shipment is an avoided term).
    /// </summary>
    public class RatingContext
    {
        public string PlantCode { get; set; }
        public string ServiceName { get; set; }
        public int PackageCount { get; set; }

        /// <summary>
        /// Accepted as given; CalculatedRate.Apply does not compute CWT eligibility itself
        /// (see docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md).
        /// </summary>
        public bool IsCwtEligible { get; set; }
    }
}
