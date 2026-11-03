namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// Plant Charges for one plant/service lookup: the markup percentage, per-package charge,
    /// and per-shipment charge a ChargeSchedule returns. See CONTEXT.md for the Plant Charges term.
    /// </summary>
    public class PlantServiceCharges
    {
        public decimal MarkupPercent { get; set; }
        public decimal PerPackageCharge { get; set; }
        public decimal PerShipmentCharge { get; set; }
    }
}
