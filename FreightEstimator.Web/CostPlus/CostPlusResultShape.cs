using System;
using System.Collections.Generic;
using System.Linq;
using AuthenticationServer.Models.Services;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// How a result row looks to FileMaker (#34, ADR 0005), copied from the console app's saveResults: methods named XX dropped,
    /// UPS lines cheapest first, the per-Plant filters for ALP and FTW, at most 15 slots (the table's width; the console app's
    /// cap of 20 is a GUI limit), the address classification digit in each slot's Note, and ClassifiedResidential and
    /// SubstitutedZip. Lives in the CostPlus adapter: the domain code never sees FileMaker's layout.
    /// </summary>
    internal static class CostPlusResultShape
    {
        public const int MaxSlots = 15;

        /// <summary>The console app drops any method with this name.</summary>
        private const string DroppedMethod = "XX";

        public class Slot
        {
            public string ServiceName;
            public decimal Rate;
            /// <summary>UPS's address classification as a digit: 0 unknown, 1 commercial, 2 residential. 2 means residential.</summary>
            public int Note;
        }

        public class Shaped
        {
            public List<Slot> Slots = new List<Slot>();
            public bool ClassifiedResidential;
            public string SubstitutedZip;
        }

        public static Shaped Shape(string plantCode, IEnumerable<UpsParcelServiceEstimate> services, string addressClassification, string toZip)
        {
            int note = ClassificationDigit(addressClassification);

            IEnumerable<UpsParcelServiceEstimate> kept = services.Where(s => s.ServiceName != DroppedMethod);

            IEnumerable<UpsParcelServiceEstimate> ordered = kept.OrderBy(s => s.CalculatedRate.RoundedAmount);

            // The console app's ALP and FTW branches put UPSGroundFreight first, then the other UPS services by price (the LTL
            // carriers they also list are not part of this phase). UPS-only parcel quotes carry no UPSGroundFreight yet, so today
            // this changes nothing; it keeps the order right when it appears.
            if (plantCode == "ALP" || plantCode == "FTW")
            {
                string groundFreight = RateCalculationsChargeSchedule.GroundFreightServiceName;
                ordered = ordered.Where(s => s.ServiceName == groundFreight).Concat(ordered.Where(s => s.ServiceName != groundFreight));
            }

            var shaped = new Shaped
            {
                Slots = ordered
                            .Take(MaxSlots)
                            .Select(s => new Slot { ServiceName = s.ServiceName, Rate = s.CalculatedRate.RoundedAmount, Note = note })
                            .ToList(),
                SubstitutedZip = toZip
            };
            // The console app reads it off the first slot.
            shaped.ClassifiedResidential = shaped.Slots.Count > 0 && shaped.Slots[0].Note == 2;
            return shaped;
        }

        public static int ClassificationDigit(string classification)
        {
            if (string.Equals(classification, "Residential", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(classification, "Commercial", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }
    }
}
