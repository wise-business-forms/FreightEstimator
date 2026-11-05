using System.Text;

namespace FreightEstimator.QuoteTests.Replay
{
    public enum DeltaCategory
    {
        /// <summary>Same value from both formulas.</summary>
        Identical,
        /// <summary>Within a cent: the unified side rounds half-up to the cent (#13); the legacy side shows, or stored, something less exact.</summary>
        RoundingNoise,
        /// <summary>A real difference that a decision already on record accounts for.</summary>
        KnownCause,
        /// <summary>A real difference nothing on record accounts for. The ADR 0003 gate fails while any exist.</summary>
        Unexplained
    }

    /// <summary>One service of one stored response, priced by today's formula and by the unified module.</summary>
    public class ReplayRow
    {
        public const string Grid1Single = "GRID1Single";
        public const string Grid2 = "GRID2";
        public const string Ltl = "LTL";

        public string CallSite { get; set; }
        public string Plant { get; set; }
        public string Service { get; set; }
        public bool IsCwtEligible { get; set; }
        public decimal CarrierRate { get; set; }

        /// <summary>The markup percent the unified module applied.</summary>
        public decimal MarkupPercent { get; set; }

        /// <summary>What today's formula gives, as the Plant page shows it.</summary>
        public decimal Legacy { get; set; }

        /// <summary>What the unified module gives, rounded half-up to the cent.</summary>
        public decimal Unified { get; set; }

        /// <summary>Where the response came from, e.g. "Requests_Rate 1234", so a divergence can be traced.</summary>
        public string Source { get; set; }

        public decimal Delta => Unified - Legacy;
        public DeltaCategory Category => DeltaClassifier.Classify(this);
    }

    /// <summary>
    /// Decides whether a difference between the legacy and unified values is explained. The gate in ADR 0003 is
    /// "every divergence is explained", so this is deliberately narrow: only causes written down are accepted.
    /// </summary>
    public static class DeltaClassifier
    {
        /// <summary>A cent: what rounding half-up against a truncated or unrounded value can move a price.</summary>
        private const decimal OneCent = 0.01m;
        private const decimal Exact = 0.0005m;

        public static DeltaCategory Classify(ReplayRow row)
        {
            decimal delta = row.Delta;
            if (Math.Abs(delta) < Exact) return DeltaCategory.Identical;
            if (Math.Abs(delta) <= OneCent) return DeltaCategory.RoundingNoise;
            return MarkupLeftOffNonCwt(row) ? DeltaCategory.KnownCause : DeltaCategory.Unexplained;
        }

        public static string Explain(ReplayRow row)
        {
            switch (Classify(row))
            {
                case DeltaCategory.Identical: return "";
                case DeltaCategory.RoundingNoise: return "Rounding to the cent (#13).";
                case DeltaCategory.KnownCause:
                    return "GRID1 single plant gives non-CWT services no markup; the unified module applies it, as decided in #14.";
                default: return "Nothing on record explains this difference.";
            }
        }

        /// <summary>
        /// GRID1 single plant applies markup only to CWT-eligible services (docs/issues/0001), and #14 decided non-CWT
        /// markup is intended. So the whole difference should be the markup, to within a cent of rounding.
        /// </summary>
        private static bool MarkupLeftOffNonCwt(ReplayRow row)
        {
            if (row.CallSite != ReplayRow.Grid1Single || row.IsCwtEligible || row.MarkupPercent <= 0) return false;
            decimal markup = row.CarrierRate * row.MarkupPercent / 100m;
            return Math.Abs(row.Delta - markup) <= OneCent;
        }
    }

    /// <summary>The rows of one call site, plant, service and outcome: how many, and how far apart the two formulas were.</summary>
    public class ReplayGroup
    {
        public string CallSite { get; set; }
        public string Plant { get; set; }
        public string Service { get; set; }
        public DeltaCategory Category { get; set; }
        public int Count { get; set; }
        public decimal MinDelta { get; set; }
        public decimal MaxDelta { get; set; }
        public string Explanation { get; set; }
    }

    /// <summary>The evidence the ADR 0003 cutover gate asks for, grouped so unexplained differences are the first thing seen.</summary>
    public class ReplayReport
    {
        private static readonly string[] CallSites = { ReplayRow.Grid1Single, ReplayRow.Grid2, ReplayRow.Ltl };

        private readonly List<ReplayRow> _rows;
        public IReadOnlyList<ReplayGroup> Groups { get; }

        private ReplayReport(List<ReplayRow> rows, List<ReplayGroup> groups)
        {
            _rows = rows;
            Groups = groups;
        }

        public static ReplayReport Build(IEnumerable<ReplayRow> rows)
        {
            List<ReplayRow> all = rows.ToList();
            List<ReplayGroup> groups = all
                .GroupBy(r => new { r.CallSite, r.Plant, r.Service, r.Category })
                .Select(g => new ReplayGroup
                {
                    CallSite = g.Key.CallSite,
                    Plant = g.Key.Plant,
                    Service = g.Key.Service,
                    Category = g.Key.Category,
                    Count = g.Count(),
                    MinDelta = g.Min(r => r.Delta),
                    MaxDelta = g.Max(r => r.Delta),
                    Explanation = DeltaClassifier.Explain(g.First())
                })
                .OrderByDescending(g => g.Category == DeltaCategory.Unexplained)
                .ThenBy(g => g.CallSite, StringComparer.Ordinal)
                .ThenBy(g => g.Plant, StringComparer.Ordinal)
                .ThenBy(g => g.Service, StringComparer.Ordinal)
                .ThenBy(g => g.Category)
                .ToList();
            return new ReplayReport(all, groups);
        }

        public int UnexplainedCount(string callSite) =>
            _rows.Count(r => r.CallSite == callSite && r.Category == DeltaCategory.Unexplained);

        /// <summary>The unexplained rows themselves, so each can be traced to its stored response.</summary>
        public IEnumerable<ReplayRow> Unexplained => _rows.Where(r => r.Category == DeltaCategory.Unexplained);

        /// <summary>A written finding per call site, to attach to ADR 0003's gate.</summary>
        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Replay of stored carrier responses: legacy formula vs unified Calculated Rate");
            sb.AppendLine();
            sb.AppendLine("Unified values are rounded half-up to the cent (#13). Delta = unified - legacy.");

            foreach (string callSite in CallSites)
            {
                List<ReplayRow> rows = _rows.Where(r => r.CallSite == callSite).ToList();
                sb.AppendLine();
                sb.AppendLine("## " + callSite);
                sb.AppendLine();

                if (rows.Count == 0)
                {
                    sb.AppendLine("There are no stored responses for this call site, so there is no evidence either way. Not ready.");
                    continue;
                }

                int unexplained = rows.Count(r => r.Category == DeltaCategory.Unexplained);
                sb.AppendLine("Rows: " + rows.Count + ". Identical: " + rows.Count(r => r.Category == DeltaCategory.Identical)
                    + ". Rounding noise: " + rows.Count(r => r.Category == DeltaCategory.RoundingNoise)
                    + ". Known cause: " + rows.Count(r => r.Category == DeltaCategory.KnownCause)
                    + ". **Unexplained: " + unexplained + ".**");
                sb.AppendLine();
                sb.AppendLine(unexplained == 0
                    ? "Gate: every divergence is explained."
                    : "Gate: NOT READY. " + unexplained + " divergence(s) are unexplained.");
                sb.AppendLine();
                sb.AppendLine("| Plant | Service | Rows | Min delta | Max delta | Outcomes | Why |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var ps in rows.GroupBy(r => new { r.Plant, r.Service })
                    .OrderByDescending(g => g.Any(r => r.Category == DeltaCategory.Unexplained))
                    .ThenBy(g => g.Key.Plant, StringComparer.Ordinal).ThenBy(g => g.Key.Service, StringComparer.Ordinal))
                {
                    string outcomes = string.Join(", ", ps.GroupBy(r => r.Category).OrderByDescending(g => g.Key == DeltaCategory.Unexplained).ThenBy(g => g.Key).Select(g => g.Key + " " + g.Count()));
                    string why = string.Join(" ", ps.Where(r => r.Category != DeltaCategory.Identical).Select(r => DeltaClassifier.Explain(r)).Distinct());
                    sb.AppendLine("| " + ps.Key.Plant + " | " + ps.Key.Service + " | " + ps.Count() + " | " + ps.Min(r => r.Delta).ToString("0.00") + " | " + ps.Max(r => r.Delta).ToString("0.00") + " | " + outcomes + " | " + why + " |");
                }

                sb.AppendLine();
                sb.AppendLine("Every service, with legacy value, unified value and delta:");
                sb.AppendLine();
                sb.AppendLine("| Source | Plant | Service | CWT | Carrier | Legacy | Unified | Delta | Outcome |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
                foreach (ReplayRow r in rows.OrderByDescending(r => r.Category == DeltaCategory.Unexplained).ThenBy(r => r.Plant, StringComparer.Ordinal).ThenBy(r => r.Service, StringComparer.Ordinal).ThenBy(r => r.Source, StringComparer.Ordinal))
                {
                    sb.AppendLine("| " + r.Source + " | " + r.Plant + " | " + r.Service + " | " + (r.IsCwtEligible ? "yes" : "no") + " | " + r.CarrierRate.ToString("0.00") + " | " + r.Legacy.ToString("0.00") + " | " + r.Unified.ToString("0.00") + " | " + r.Delta.ToString("0.00") + " | " + r.Category + " |");
                }

                List<ReplayRow> bad = rows.Where(r => r.Category == DeltaCategory.Unexplained).Take(20).ToList();
                if (bad.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Unexplained rows (first " + bad.Count + "):");
                    sb.AppendLine();
                    foreach (ReplayRow r in bad)
                    {
                        sb.AppendLine("* " + r.Source + ": " + r.Plant + " " + r.Service + (r.IsCwtEligible ? " (CWT)" : "")
                            + ", carrier " + r.CarrierRate.ToString("0.00") + ", legacy " + r.Legacy.ToString("0.00") + ", unified " + r.Unified.ToString("0.00"));
                    }
                }
            }

            return sb.ToString();
        }
    }
}
