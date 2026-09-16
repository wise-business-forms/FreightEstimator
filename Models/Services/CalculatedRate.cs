namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// The final price shown to the requester for a Quote, after applying Plant Charges to a
    /// Carrier Rate (see CONTEXT.md). Replaces the four divergent per-call-site markup formulas
    /// tracked in docs/issues/0001-ups-markup-divergence.md.
    /// </summary>
    public class CalculatedRate
    {
        public decimal CarrierRate { get; }
        public decimal PerPackageCharge { get; }
        public decimal PerShipmentCharge { get; }
        public decimal MarkupPercent { get; }
        public decimal Amount { get; }

        private CalculatedRate(decimal carrierRate, decimal perPackageCharge, decimal perShipmentCharge, decimal markupPercent, decimal amount)
        {
            CarrierRate = carrierRate;
            PerPackageCharge = perPackageCharge;
            PerShipmentCharge = perShipmentCharge;
            MarkupPercent = markupPercent;
            Amount = amount;
        }

        /// <summary>
        /// Canonical formula, applied unconditionally regardless of CWT eligibility or service
        /// name: CarrierRate + CarrierRate x MarkupPercent + PerPackageCharge x PackageCount + PerShipmentCharge.
        /// </summary>
        public static CalculatedRate Apply(decimal carrierRate, IChargeSchedule chargeSchedule, RatingContext context)
        {
            PlantServiceCharges charges = chargeSchedule.GetCharges(context.PlantCode, context.ServiceName, context.IsCwtEligible);

            decimal amount = carrierRate
                + carrierRate * (charges.MarkupPercent / 100m)
                + charges.PerPackageCharge * context.PackageCount
                + charges.PerShipmentCharge;

            return new CalculatedRate(carrierRate, charges.PerPackageCharge, charges.PerShipmentCharge, charges.MarkupPercent, amount);
        }
    }
}
