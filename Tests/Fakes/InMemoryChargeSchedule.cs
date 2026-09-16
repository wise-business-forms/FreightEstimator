using System.Collections.Generic;
using AuthenticationServer.Models.Services;

namespace FreightEstimator.Tests.Fakes
{
    /// <summary>
    /// In-memory ChargeSchedule test adapter (see docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md).
    /// Returns zeroed-out charges for any plant/service/CWT combination it wasn't told about.
    /// </summary>
    public class InMemoryChargeSchedule : IChargeSchedule
    {
        private readonly Dictionary<(string PlantCode, string ServiceName, bool IsCwtEligible), PlantServiceCharges> _charges = new();

        public InMemoryChargeSchedule With(string plantCode, string serviceName, bool isCwtEligible, decimal markupPercent, decimal perPackageCharge, decimal perShipmentCharge)
        {
            _charges[(plantCode, serviceName, isCwtEligible)] = new PlantServiceCharges
            {
                MarkupPercent = markupPercent,
                PerPackageCharge = perPackageCharge,
                PerShipmentCharge = perShipmentCharge
            };
            return this;
        }

        public PlantServiceCharges GetCharges(string plantCode, string serviceName, bool isCwtEligible)
        {
            return _charges.TryGetValue((plantCode, serviceName, isCwtEligible), out var charges)
                ? charges
                : new PlantServiceCharges();
        }
    }
}
