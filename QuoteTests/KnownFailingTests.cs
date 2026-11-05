namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Paths the Plant page cannot complete today (#15). They are NOT pinned as correct: each states what
    /// should happen, is ignored so the suite stays green, and records how it fails now. Remove the Ignore
    /// when the path is fixed; the test then guards the fix.
    /// </summary>
    public class KnownFailingTests
    {
        [Test, Ignore("Known failing: multi-plant (\"rate from multiple locations\") throws KeyNotFoundException while applying plant charges per service. See docs/issues/0001 item 2 and docs/research/calculation-audit.md.")]
        public void MultiPlantParcelQuoteCompletes()
        {
            QuoteCase c = QuoteCase.All("parcel").First(x => x.CaseId == "444586");

            Assert.That(QuoteFlow.Run(c, multiplePlants: true).Count, Is.GreaterThan(0));
        }

        [Test, Ignore("Known failing: GRID2 (Ground Freight) with a fractional package weight (33.5 lb) throws FormatException when the weight is parsed. See docs/research/calculation-audit.md.")]
        public void GroundFreightQuoteWithAFractionalWeightCompletes()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First(x => x.CaseId == "444626");
            c.PackageWeight = 33.5f;

            Assert.That(QuoteFlow.Run(c).Count, Is.GreaterThan(0));
        }
    }
}
