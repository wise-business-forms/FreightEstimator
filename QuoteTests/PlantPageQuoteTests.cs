using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Characterization tests (#15): the Calculated Rate per service the Plant page produces today for real
    /// recorded UPS quotes, as each path computes it, warts and all. They pin behaviour so the extraction in
    /// #16 cannot change a number; a passing test is "unchanged", not "correct".
    ///
    /// Expected values live in Tests/Recorded/quotes/expected/{RequestId}.json. To regenerate them after an
    /// intended pricing change, run with QUOTETESTS_RECORD=1 and review the diff.
    ///
    /// Warts these numbers pin (each was cross-checked against an independent calculation from the recorded
    /// UPS response and the seeded charges; all other services agree with the documented formula):
    ///  - GRID 2 (Ground Freight) applies the plant's markup to NON-CWT quotes (published x 1 + markup%), where
    ///    GRID 1 gives non-CWT quotes no markup. Cases 444536, 444595 and 444597. See docs/issues/0001 and #14.
    /// </summary>
    public class PlantPageQuoteTests
    {
        private static bool Record => Environment.GetEnvironmentVariable("QUOTETESTS_RECORD") == "1";

        public static IEnumerable<TestCaseData> ParcelCases() => Cases("parcel");
        public static IEnumerable<TestCaseData> GroundFreightCases() => Cases("groundfreight");

        private static IEnumerable<TestCaseData> Cases(string kind) =>
            QuoteCase.All(kind).Select(c => new TestCaseData(c).SetName(kind + " " + c.RequestId + " " + c.Plant + " " + c.Packages + " pkgs"));

        [TestCaseSource(nameof(ParcelCases))]
        public void UpsParcelQuote_GRID1_SinglePlant(QuoteCase c) => AssertPinned(c);

        [TestCaseSource(nameof(GroundFreightCases))]
        public void UpsGroundFreightQuote_GRID2(QuoteCase c) => AssertPinned(c);

        private static void AssertPinned(QuoteCase c)
        {
            JObject actual = QuoteFlow.Run(c);
            Assert.That(actual.Count, Is.GreaterThan(0), "the flow returned no services");

            string expectedFile = Path.Combine(TestContext.CurrentContext.TestDirectory, "Recorded", "quotes", "expected", c.RequestId + ".json");
            if (Record)
            {
                string source = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Tests", "Recorded", "quotes", "expected", c.RequestId + ".json"));
                Directory.CreateDirectory(Path.GetDirectoryName(source));
                File.WriteAllText(source, actual.ToString(Newtonsoft.Json.Formatting.Indented) + Environment.NewLine);
                Assert.Inconclusive("Recorded " + source);
            }

            Assert.That(File.Exists(expectedFile), "no expected file for " + c.RequestId + "; run with QUOTETESTS_RECORD=1");
            JObject expected = JObject.Parse(File.ReadAllText(expectedFile));

            Assert.That(actual.Properties().Select(p => p.Name), Is.EquivalentTo(expected.Properties().Select(p => p.Name)), "services quoted");
            foreach (JProperty service in expected.Properties())
            {
                Assert.That((decimal)actual[service.Name]["calculatedRate"], Is.EqualTo((decimal)service.Value["calculatedRate"]), service.Name + " calculated rate");
                Assert.That((string)actual[service.Name]["cwt"], Is.EqualTo((string)service.Value["cwt"]), service.Name + " CWT");
                Assert.That((string)actual[service.Name]["chargeSchedule"], Is.EqualTo((string)service.Value["chargeSchedule"]), service.Name + " charge schedule");
                Assert.That((string)actual[service.Name]["markupPercent"], Is.EqualTo((string)service.Value["markupPercent"]), service.Name + " markup %");
                Assert.That((string)actual[service.Name]["perPackageCharge"], Is.EqualTo((string)service.Value["perPackageCharge"]), service.Name + " per-package charge");
                Assert.That((string)actual[service.Name]["perShipmentCharge"], Is.EqualTo((string)service.Value["perShipmentCharge"]), service.Name + " per-shipment charge");
            }
        }
    }
}
