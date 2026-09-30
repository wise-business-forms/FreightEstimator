using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.Models.Reports
{
    /// <summary>
    /// What makes two UPS Quotes a Match Group (see CONTEXT.md): they sent UPS the same inputs.
    /// WeightsKey is each package weight rounded up to a whole pound, sorted, comma-joined.
    /// </summary>
    public sealed class MatchKey : IEquatable<MatchKey>
    {
        public MatchKey(string plantCode, string shipToZip, int packageCount, string weightsKey, bool residentialIndicatorSent)
        {
            PlantCode = plantCode;
            ShipToZip = shipToZip;
            PackageCount = packageCount;
            WeightsKey = weightsKey;
            ResidentialIndicatorSent = residentialIndicatorSent;
        }

        public string PlantCode { get; }
        public string ShipToZip { get; }
        public int PackageCount { get; }
        public string WeightsKey { get; }
        public bool ResidentialIndicatorSent { get; }

        public bool Equals(MatchKey other) =>
            other != null
            && string.Equals(PlantCode, other.PlantCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ShipToZip, other.ShipToZip, StringComparison.OrdinalIgnoreCase)
            && PackageCount == other.PackageCount
            && string.Equals(WeightsKey, other.WeightsKey, StringComparison.Ordinal)
            && ResidentialIndicatorSent == other.ResidentialIndicatorSent;

        public override bool Equals(object obj) => Equals(obj as MatchKey);

        public override int GetHashCode() =>
            ((PlantCode ?? "").ToUpperInvariant() + "|" + (ShipToZip ?? "").ToUpperInvariant() + "|" + PackageCount + "|" + WeightsKey + "|" + ResidentialIndicatorSent).GetHashCode();
    }

    public sealed class ServiceRate
    {
        public ServiceRate(string serviceCode, string serviceName, decimal? publishedRate, decimal? negotiatedRate)
        {
            ServiceCode = serviceCode;
            ServiceName = serviceName;
            PublishedRate = publishedRate;
            NegotiatedRate = negotiatedRate;
        }

        public string ServiceCode { get; }
        public string ServiceName { get; }
        public decimal? PublishedRate { get; }
        public decimal? NegotiatedRate { get; }
    }

    /// <summary>One UPS Quote from Requests_Rate, as extracted into the derived tables.</summary>
    public sealed class QuoteFacts
    {
        public QuoteFacts(int requestId, DateTime requestDate, MatchKey key, IEnumerable<ServiceRate> services)
        {
            RequestId = requestId;
            RequestDate = requestDate;
            Key = key;
            // One rate per service code; if a response ever lists a code twice, keep the first.
            Services = (services ?? Enumerable.Empty<ServiceRate>())
                .GroupBy(s => s.ServiceCode)
                .Select(g => g.First())
                .ToList();
        }

        public int RequestId { get; }
        public DateTime RequestDate { get; }
        public MatchKey Key { get; }
        public IReadOnlyList<ServiceRate> Services { get; }
    }

    /// <summary>A QuoteServiceCalculation row that might belong to a UPS Quote's service.</summary>
    public sealed class CalculationCandidate
    {
        public int QuoteServiceCalculationId { get; set; }
        public string PlantCode { get; set; }
        public string ServiceCode { get; set; }
        public decimal? CarrierRate { get; set; }
        public bool? IsNegotiatedRate { get; set; }
        public decimal? MarkupPercent { get; set; }
        public decimal? PerPackageCharge { get; set; }
        public decimal? PerShipmentCharge { get; set; }
        public decimal CalculatedRate { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public enum RateChangeCause
    {
        /// <summary>Published Rate changed: a UPS list-price move.</summary>
        UpsPriceChange,
        /// <summary>Negotiated Rate changed but Published didn't: our UPS discount moved.</summary>
        UpsNegotiatedChange,
        /// <summary>A Plant Charges component (markup %, per-package, per-shipment) changed.</summary>
        OurChargesChanged,
        /// <summary>Same Carrier Rate and charges, different Calculated Rate: our code changed.</summary>
        OurCalculationChanged,
        /// <summary>No Calculated Rate matched on one side, so only the UPS causes could be decided.</summary>
        Unattributable
    }

    public enum ChargeComponent
    {
        MarkupPercent,
        PerPackageCharge,
        PerShipmentCharge
    }

    /// <summary>The report's wording for each attribution label (see CONTEXT.md, "Rate Change").</summary>
    public static class RateChangeLabels
    {
        public static string For(RateChangeCause cause, IEnumerable<ChargeComponent> changedCharges)
        {
            switch (cause)
            {
                case RateChangeCause.UpsPriceChange: return "UPS price change";
                case RateChangeCause.UpsNegotiatedChange: return "UPS negotiated change";
                case RateChangeCause.OurChargesChanged:
                    return "Our charges changed (" + string.Join(", ", changedCharges.Select(For)) + ")";
                case RateChangeCause.OurCalculationChanged: return "Our calculation changed";
                case RateChangeCause.Unattributable: return "Unattributable";
                default: return cause.ToString();
            }
        }

        public static string For(ChargeComponent component)
        {
            switch (component)
            {
                case ChargeComponent.MarkupPercent: return "markup %";
                case ChargeComponent.PerPackageCharge: return "per-package";
                case ChargeComponent.PerShipmentCharge: return "per-shipment";
                default: return component.ToString();
            }
        }

        /// <summary>The badge style both report pages use: UPS causes, our causes, or unknown.</summary>
        public static string CssClassFor(RateChangeCause cause)
        {
            switch (cause)
            {
                case RateChangeCause.OurChargesChanged:
                case RateChangeCause.OurCalculationChanged: return "cause-ours";
                case RateChangeCause.Unattributable: return "cause-unknown";
                default: return "cause-ups";
            }
        }
    }

    public sealed class RateChange
    {
        public RateChange(IReadOnlyCollection<RateChangeCause> causes, IReadOnlyCollection<ChargeComponent> changedCharges,
                          bool publishedChanged, bool negotiatedChanged, bool calculatedChanged)
        {
            Causes = causes;
            ChangedCharges = changedCharges;
            PublishedChanged = publishedChanged;
            NegotiatedChanged = negotiatedChanged;
            CalculatedChanged = calculatedChanged;
        }

        public bool PublishedChanged { get; }
        public bool NegotiatedChanged { get; }

        /// <summary>Only ever true when both sides have a matched Calculated Rate.</summary>
        public bool CalculatedChanged { get; }

        public IReadOnlyCollection<RateChangeCause> Causes { get; }

        /// <summary>The Plant Charges components behind OurChargesChanged; empty otherwise.</summary>
        public IReadOnlyCollection<ChargeComponent> ChangedCharges { get; }
    }

    public sealed class TimelineEntry
    {
        public TimelineEntry(QuoteFacts quote, ServiceRate rate, CalculationCandidate calculation, RateChange change)
        {
            Quote = quote;
            Rate = rate;
            Calculation = calculation;
            Change = change;
        }

        public QuoteFacts Quote { get; }
        public ServiceRate Rate { get; }

        /// <summary>The Calculated Rate matched to this Quote's service; null when none or several qualified.</summary>
        public CalculationCandidate Calculation { get; }

        /// <summary>Change from the previous entry; null for the first entry or when nothing changed.</summary>
        public RateChange Change { get; }
    }

    /// <summary>One service's rates across a Match Group, oldest first, skipping Quotes that didn't offer it.</summary>
    public sealed class ServiceTimeline
    {
        public ServiceTimeline(string serviceCode, string serviceName, IReadOnlyList<TimelineEntry> entries)
        {
            ServiceCode = serviceCode;
            ServiceName = serviceName;
            Entries = entries;
        }

        public string ServiceCode { get; }
        public string ServiceName { get; }
        public IReadOnlyList<TimelineEntry> Entries { get; }
    }

    public sealed class MatchGroup
    {
        public MatchGroup(MatchKey key, IReadOnlyList<QuoteFacts> quotes, IReadOnlyList<ServiceTimeline> services)
        {
            Key = key;
            Quotes = quotes;
            Services = services;
        }

        public MatchKey Key { get; }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<QuoteFacts> Quotes { get; }

        public IReadOnlyList<ServiceTimeline> Services { get; }

        public bool HasRateChanges => Services.Any(s => s.Entries.Any(e => e.Change != null));
    }

    public static class RateChanges
    {
        /// <summary>
        /// How long after a Requests_Rate row its QuoteServiceCalculation rows can be logged. Both are
        /// written in the same web request, a few calls apart. See Database/004 for the check query.
        /// </summary>
        public static readonly TimeSpan CalculationWindow = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Groups Quotes into Match Groups and attributes each Rate Change. A calculation that qualifies
        /// for more than one Quote is ambiguous and attached to none, so pass as competingQuotes any
        /// other Quotes from the same plant near in time (other zips, just outside a date range): they
        /// take part in that check but aren't grouped.
        /// </summary>
        public static List<MatchGroup> Build(IEnumerable<QuoteFacts> quotes, IEnumerable<CalculationCandidate> calculations,
                                             IEnumerable<QuoteFacts> competingQuotes = null)
        {
            var quoteList = quotes.ToList();
            var calculationsByPlantAndService = (calculations ?? Enumerable.Empty<CalculationCandidate>())
                .ToLookup(c => PlantAndService(c.PlantCode, c.ServiceCode));

            var qualifyingByPair = quoteList
                .Concat(competingQuotes ?? Enumerable.Empty<QuoteFacts>())
                .GroupBy(q => q.RequestId)
                .Select(g => g.First())
                .SelectMany(q => q.Services.Select(s => new { Quote = q, Rate = s }))
                .ToDictionary(x => Tuple.Create(x.Quote.RequestId, x.Rate.ServiceCode),
                              x => calculationsByPlantAndService[PlantAndService(x.Quote.Key.PlantCode, x.Rate.ServiceCode)]
                                  .Where(c => Qualifies(c, x.Quote, x.Rate)).ToList());
            var pairsPerCalculation = qualifyingByPair.Values
                .SelectMany(cs => cs)
                .GroupBy(c => c)
                .ToDictionary(g => g.Key, g => g.Count());

            Func<QuoteFacts, ServiceRate, CalculationCandidate> matchCalculation = (quote, rate) =>
            {
                var qualifying = qualifyingByPair[Tuple.Create(quote.RequestId, rate.ServiceCode)];
                return qualifying.Count == 1 && pairsPerCalculation[qualifying[0]] == 1 ? qualifying[0] : null;
            };

            return quoteList
                .GroupBy(q => q.Key)
                .Select(g =>
                {
                    var ordered = g.OrderBy(q => q.RequestDate).ThenBy(q => q.RequestId).ToList();
                    return new MatchGroup(g.Key, ordered, BuildTimelines(ordered, matchCalculation));
                })
                .ToList();
        }

        private static string PlantAndService(string plantCode, string serviceCode) =>
            (plantCode ?? "").Trim().ToUpperInvariant() + "|" + (serviceCode ?? "").Trim().ToUpperInvariant();

        /// <summary>Same plant and service (via the lookup), logged in the window, on the Carrier Rate it says it used.</summary>
        private static bool Qualifies(CalculationCandidate c, QuoteFacts quote, ServiceRate rate)
        {
            if (c.CreatedDate < quote.RequestDate || c.CreatedDate > quote.RequestDate + CalculationWindow) return false;
            var carrierRateUsed = c.IsNegotiatedRate == true ? rate.NegotiatedRate : rate.PublishedRate;
            return c.CarrierRate.HasValue && carrierRateUsed.HasValue && !Differs(c.CarrierRate, carrierRateUsed);
        }

        private static List<ServiceTimeline> BuildTimelines(List<QuoteFacts> orderedQuotes, Func<QuoteFacts, ServiceRate, CalculationCandidate> matchCalculation)
        {
            return orderedQuotes
                .SelectMany(q => q.Services.Select(s => new { Quote = q, Rate = s }))
                .GroupBy(x => x.Rate.ServiceCode)
                .Select(serviceGroup =>
                {
                    var entries = new List<TimelineEntry>();
                    TimelineEntry previous = null;
                    foreach (var x in serviceGroup)
                    {
                        var calculation = matchCalculation(x.Quote, x.Rate);
                        var change = previous == null ? null : Compare(previous.Rate, previous.Calculation, x.Rate, calculation);
                        previous = new TimelineEntry(x.Quote, x.Rate, calculation, change);
                        entries.Add(previous);
                    }
                    return new ServiceTimeline(serviceGroup.Key, serviceGroup.First().Rate.ServiceName, entries);
                })
                .ToList();
        }

        private static RateChange Compare(ServiceRate previous, CalculationCandidate previousCalculation,
                                          ServiceRate current, CalculationCandidate currentCalculation)
        {
            bool publishedChanged = Differs(previous.PublishedRate, current.PublishedRate);
            bool negotiatedChanged = Differs(previous.NegotiatedRate, current.NegotiatedRate);
            bool bothCalculated = previousCalculation != null && currentCalculation != null;
            bool calculatedChanged = bothCalculated && Differs(previousCalculation.CalculatedRate, currentCalculation.CalculatedRate);

            if (!publishedChanged && !negotiatedChanged && !calculatedChanged) return null;

            var causes = new List<RateChangeCause>();
            var changedCharges = new List<ChargeComponent>();

            if (publishedChanged) causes.Add(RateChangeCause.UpsPriceChange);
            else if (negotiatedChanged) causes.Add(RateChangeCause.UpsNegotiatedChange);

            if (!bothCalculated)
            {
                causes.Add(RateChangeCause.Unattributable);
                return new RateChange(causes, changedCharges, publishedChanged, negotiatedChanged, calculatedChanged);
            }

            if (previousCalculation.MarkupPercent != currentCalculation.MarkupPercent) changedCharges.Add(ChargeComponent.MarkupPercent);
            if (previousCalculation.PerPackageCharge != currentCalculation.PerPackageCharge) changedCharges.Add(ChargeComponent.PerPackageCharge);
            if (previousCalculation.PerShipmentCharge != currentCalculation.PerShipmentCharge) changedCharges.Add(ChargeComponent.PerShipmentCharge);
            if (changedCharges.Count > 0) causes.Add(RateChangeCause.OurChargesChanged);

            bool baseSwitched = previousCalculation.IsNegotiatedRate != currentCalculation.IsNegotiatedRate;
            bool sameInputs = changedCharges.Count == 0 && !Differs(previousCalculation.CarrierRate, currentCalculation.CarrierRate);
            if (baseSwitched || (sameInputs && calculatedChanged)) causes.Add(RateChangeCause.OurCalculationChanged);

            return new RateChange(causes, changedCharges, publishedChanged, negotiatedChanged, calculatedChanged);
        }

        /// <summary>A change is at least one cent; a rate missing on either side can't be compared.</summary>
        public static bool Differs(decimal? previous, decimal? current) =>
            previous.HasValue && current.HasValue
            && Math.Abs(Math.Round(previous.Value, 2) - Math.Round(current.Value, 2)) >= 0.01m;
    }
}
