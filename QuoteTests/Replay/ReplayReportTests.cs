using FreightEstimator.QuoteTests.Replay;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The replay harness's judgement and its report (#26), with no database and no carrier: given what the legacy
    /// formula and the unified module each produced for a stored response, is the difference explained?
    /// </summary>
    public class ReplayReportTests
    {
        private static ReplayRow Row(string callSite = "GRID1Single", string plant = "BUT", string service = "UPSGround", bool cwt = false,
            decimal carrier = 100m, decimal markup = 5m, decimal legacy = 100m, decimal unified = 100m) =>
            new ReplayRow { CallSite = callSite, Plant = plant, Service = service, IsCwtEligible = cwt, CarrierRate = carrier, MarkupPercent = markup, Legacy = legacy, Unified = unified, Source = "r1" };

        [Test]
        public void EqualValuesAreIdentical()
        {
            Assert.That(DeltaClassifier.Classify(Row(legacy: 107m, unified: 107m)), Is.EqualTo(DeltaCategory.Identical));
        }

        [TestCase(143.672, 143.67)]   // the Plant page shows it unrounded, the API rounds half-up
        [TestCase(143.675, 143.68)]
        [TestCase(143.679, 143.67)]   // a value truncated to the cent upstream sits just under the rounded one
        public void AFractionOfACentIsRoundingNoise(double legacy, double unified)
        {
            Assert.That(DeltaClassifier.Classify(Row(legacy: (decimal)legacy, unified: (decimal)unified)), Is.EqualTo(DeltaCategory.RoundingNoise));
        }

        [Test]
        public void ADifferenceOfExactlyTheMarkupGRID1LeavesOffNonCwtIsAKnownCause()
        {
            // Carrier rate 200 at 11.5% markup: the unified value is 23.00 higher (#14: plant AND's non-CWT markup).
            ReplayRow row = Row(plant: "AND", carrier: 200m, markup: 11.5m, legacy: 207m, unified: 230m);

            Assert.That(DeltaClassifier.Classify(row), Is.EqualTo(DeltaCategory.KnownCause));
            Assert.That(DeltaClassifier.Explain(row), Does.Contain("#14"));
        }

        [Test]
        public void TheMarkupExplanationAllowsForRoundingToo()
        {
            ReplayRow row = Row(carrier: 143.67m, markup: 5m, legacy: 150.8535m, unified: 158.04m);   // 143.67 * 5% = 7.1835

            Assert.That(DeltaClassifier.Classify(row), Is.EqualTo(DeltaCategory.KnownCause));
        }

        [Test]
        public void TheMarkupExplanationOnlyAppliesToNonCwtOnGRID1()
        {
            ReplayRow cwt = Row(cwt: true, carrier: 200m, markup: 10m, legacy: 207m, unified: 227m);
            ReplayRow grid2 = Row(callSite: "GRID2", carrier: 200m, markup: 10m, legacy: 207m, unified: 227m);
            ReplayRow ltl = Row(callSite: "LTL", carrier: 200m, markup: 10m, legacy: 207m, unified: 227m);

            Assert.That(DeltaClassifier.Classify(cwt), Is.EqualTo(DeltaCategory.Unexplained));
            Assert.That(DeltaClassifier.Classify(grid2), Is.EqualTo(DeltaCategory.Unexplained));
            Assert.That(DeltaClassifier.Classify(ltl), Is.EqualTo(DeltaCategory.Unexplained));
        }

        [Test]
        public void ADifferenceThatIsNotTheMarkupIsUnexplained()
        {
            ReplayRow row = Row(carrier: 200m, markup: 11.5m, legacy: 207m, unified: 240m);   // 33.00, not 23.00

            Assert.That(DeltaClassifier.Classify(row), Is.EqualTo(DeltaCategory.Unexplained));
        }

        [Test]
        public void ANonCwtRowWithNoMarkupCannotBeExplainedByMarkup()
        {
            ReplayRow row = Row(markup: 0m, carrier: 200m, legacy: 207m, unified: 210m);

            Assert.That(DeltaClassifier.Classify(row), Is.EqualTo(DeltaCategory.Unexplained));
        }

        [Test]
        public void TheReportGroupsByCallSitePlantAndServiceWithCountMinAndMax()
        {
            var rows = new[]
            {
                Row(plant: "AND", carrier: 200m, markup: 11.5m, legacy: 207m, unified: 230m),
                Row(plant: "AND", carrier: 100m, markup: 11.5m, legacy: 107m, unified: 118.5m),
                Row(plant: "BUT", legacy: 107m, unified: 107m),
                Row(plant: "AND", service: "UPS3DaySelect", carrier: 100m, markup: 11.5m, legacy: 107m, unified: 118.5m)
            };

            ReplayReport report = ReplayReport.Build(rows);

            ReplayGroup and = report.Groups.Single(g => g.Plant == "AND" && g.Service == "UPSGround");
            Assert.That(and.Count, Is.EqualTo(2));
            Assert.That(and.MinDelta, Is.EqualTo(11.5m));
            Assert.That(and.MaxDelta, Is.EqualTo(23m));
            Assert.That(and.Category, Is.EqualTo(DeltaCategory.KnownCause));
            Assert.That(report.Groups, Has.Count.EqualTo(3));
        }

        [Test]
        public void UnexplainedGroupsComeFirstAndTheGateFailsWhileAnyExist()
        {
            var rows = new[]
            {
                Row(plant: "BUT", legacy: 107m, unified: 107m),
                Row(plant: "POR", legacy: 107m, unified: 140m)
            };

            ReplayReport report = ReplayReport.Build(rows);

            Assert.That(report.Groups.First().Category, Is.EqualTo(DeltaCategory.Unexplained));
            Assert.That(report.UnexplainedCount("GRID1Single"), Is.EqualTo(1));
            Assert.That(report.ToMarkdown(), Does.Contain("NOT READY").And.Contain("POR"));
        }

        [Test]
        public void TheGatePassesWhenEveryDivergenceIsExplained()
        {
            var rows = new[]
            {
                Row(legacy: 107m, unified: 107m),
                Row(plant: "AND", carrier: 200m, markup: 11.5m, legacy: 207m, unified: 230m)
            };

            string markdown = ReplayReport.Build(rows).ToMarkdown();

            Assert.That(markdown, Does.Contain("every divergence is explained"));
            Assert.That(markdown, Does.Not.Contain("NOT READY"));
        }

        [Test]
        public void ACallSiteWithNoStoredResponsesIsNotReportedAsReady()
        {
            string markdown = ReplayReport.Build(new[] { Row(legacy: 107m, unified: 107m) }).ToMarkdown();

            Assert.That(markdown, Does.Contain("GRID2").And.Contain("no stored responses"));
        }
    }
}
