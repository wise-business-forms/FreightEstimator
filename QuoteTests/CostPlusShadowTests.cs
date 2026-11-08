using AuthenticationServer.CostPlus;
using FreightEstimator.Tests.Fakes;
using Microsoft.Extensions.Configuration;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The observe-only shadow run (#36, ADR 0005): beside the console app, the poller works out its own result for each request
    /// the console app answered and stores both for review. It never claims or writes a result row, and leaves nothing for
    /// reporting. The "console app's" result is a row answered by a live cycle (the same numbers), or changed afterwards.
    /// </summary>
    public class CostPlusShadowTests
    {
        private SandboxCostPlus _costPlus;

        [SetUp]
        public void SetUp() => _costPlus = new SandboxCostPlus();

        [TearDown]
        public void TearDown() => _costPlus.Dispose();

        private static QuoteCase Case => QuoteCase.All("parcel").Single(c => c.CaseId == "444347");

        /// <summary>A request the "console app" has answered (here: a live cycle writes the row).</summary>
        private string AnsweredRequest(QuoteCase c)
        {
            string loginId = _costPlus.Add(SandboxCostPlus.For(c));
            CostPlusCycle.Run(QuoteFlow.FakeFor(c));
            Assert.That(_costPlus.ResultOf(loginId).DateRated, Is.Not.Null);
            return loginId;
        }

        [Test]
        public void ResultsThatAgreeAreStoredAsAMatch()
        {
            QuoteCase c = Case;
            string loginId = AnsweredRequest(c);

            int compared = CostPlusCycle.RunShadow(QuoteFlow.FakeFor(c));

            Assert.That(compared, Is.EqualTo(1));
            SandboxCostPlus.ShadowRow shadow = _costPlus.ShadowOf(loginId);
            Assert.That(shadow.Diverged, Is.False, shadow.Differences);
            Assert.That(shadow.Status, Is.EqualTo("Match"));
            Assert.That(shadow.ConsoleResult, Is.EqualTo(shadow.PollerResult), "both are stored, in the same shape, so they can be compared");
            Assert.That(shadow.ConsoleResult, Does.Contain("UPS3DaySelect"));
        }

        [Test]
        public void ADivergenceIsStoredWithWhatDiffersAndIsUnexplainedUntilSomeoneExplainsIt()
        {
            QuoteCase c = Case;
            string loginId = AnsweredRequest(c);
            _costPlus.ChangeConsoleRate(loginId, 1.00m);

            CostPlusCycle.RunShadow(QuoteFlow.FakeFor(c));

            SandboxCostPlus.ShadowRow shadow = _costPlus.ShadowOf(loginId);
            Assert.That(shadow.Diverged, Is.True);
            Assert.That(shadow.Status, Is.EqualTo("Unexplained"));
            Assert.That(shadow.Differences, Does.Contain("rate").And.Contain("-1.00"));   // poller - console app

            _costPlus.Explain(loginId, "console app rounds per package");
            Assert.That(_costPlus.ShadowOf(loginId).Status, Is.EqualTo("Explained"));
        }

        [Test]
        public void TheShadowRunNeverClaimsWritesAResultRowOrLogsAQuote()
        {
            QuoteCase c = Case;
            string answered = AnsweredRequest(c);
            string waiting = _costPlus.Add(SandboxCostPlus.For(c));   // not answered by the console app yet
            SandboxCostPlus.Result before = _costPlus.ResultOf(answered);
            int claims = _costPlus.ClaimCount(answered), quotes = _costPlus.QuoteCount(answered);
            int requestLog = SandboxCostPlus.RowCount("dbo.Requests_Rate"), calcs = SandboxCostPlus.RowCount("dbo.QuoteServiceCalculation");
            int allQuotes = SandboxCostPlus.RowCount("dbo.Quote"), shadowLog = SandboxCostPlus.RowCount("dbo.CalculatedRateShadowLog");
            long reference = ReferenceSequenceValue();

            CostPlusCycle.RunShadow(QuoteFlow.FakeFor(c));

            SandboxCostPlus.Result after = _costPlus.ResultOf(answered);
            Assert.That(after.DateProcessed, Is.EqualTo(before.DateProcessed));
            Assert.That(after.Slots.Select(s => s.Rate), Is.EqualTo(before.Slots.Select(s => s.Rate)));
            Assert.That(_costPlus.ClaimCount(answered), Is.EqualTo(claims));
            Assert.That(_costPlus.QuoteCount(answered), Is.EqualTo(quotes));
            Assert.That(_costPlus.ClaimOf(waiting), Is.Null, "an unanswered request is left to the console app");
            Assert.That(_costPlus.ResultOf(waiting).DateRated, Is.Null);
            Assert.That(SandboxCostPlus.RowCount("dbo.Requests_Rate"), Is.EqualTo(requestLog), "nothing in the request log for reporting");
            Assert.That(SandboxCostPlus.RowCount("dbo.QuoteServiceCalculation"), Is.EqualTo(calcs));
            Assert.That(SandboxCostPlus.RowCount("dbo.Quote"), Is.EqualTo(allQuotes));
            Assert.That(SandboxCostPlus.RowCount("dbo.CalculatedRateShadowLog"), Is.EqualTo(shadowLog));
            Assert.That(ReferenceSequenceValue(), Is.EqualTo(reference), "no Reference Number spent");
        }

        [Test]
        public void ARequestIsComparedOnceAndProbesAreLeftOut()
        {
            QuoteCase c = Case;
            string loginId = AnsweredRequest(c);

            Assert.That(CostPlusCycle.RunShadow(QuoteFlow.FakeFor(c)), Is.EqualTo(1));
            Assert.That(CostPlusCycle.RunShadow(QuoteFlow.FakeFor(c)), Is.EqualTo(0), "already compared");

            string probe = _costPlus.Add(SandboxCostPlus.For(c), CostPlusProbe.LoginIdPrefix + Guid.NewGuid().ToString("N"));
            _costPlus.MarkAnswered(probe);
            Assert.That(CostPlusCycle.RunShadow(new FakeCarrierHttp()), Is.EqualTo(0));
            Assert.That(_costPlus.ShadowOf(probe), Is.Null);
            Assert.That(_costPlus.ShadowOf(loginId), Is.Not.Null);
        }

        [Test]
        public void ARequestThePollerCannotWorkOutIsStoredAsADivergence()
        {
            QuoteCase c = Case;
            string loginId = AnsweredRequest(c);

            CostPlusCycle.RunShadow(new FakeCarrierHttp());   // nothing set up: the UPS call throws

            SandboxCostPlus.ShadowRow shadow = _costPlus.ShadowOf(loginId);
            Assert.That(shadow.Diverged, Is.True);
            Assert.That(shadow.PollerResult, Does.Contain("GENERAL"));
        }

        private static long ReferenceSequenceValue()
        {
            using (var conn = new System.Data.SqlClient.SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["FreightEstimator"].ConnectionString))
            {
                conn.Open();
                using (var cmd = new System.Data.SqlClient.SqlCommand("SELECT CONVERT(BIGINT, current_value) FROM sys.sequences WHERE name = 'CostPlusReferenceNumber'", conn))
                    return (long)cmd.ExecuteScalar();
            }
        }

        // ---- the comparison and the mode (no database) ----

        [Test]
        public void TheComparisonNamesEachDifference()
        {
            var console = new CostPlusShadowComparison.Result { ClassifiedResidential = false };
            console.Lines.Add(new CostPlusShadowComparison.Line { Service = "UPSGround", Rate = 10m, Note = "1" });
            var poller = new CostPlusShadowComparison.Result { ClassifiedResidential = true, SubstitutedZip = "02379" };
            poller.Lines.Add(new CostPlusShadowComparison.Line { Service = "UPSGround", Rate = 12.5m, Note = "2" });

            var differences = CostPlusShadowComparison.Differences(console, poller);

            Assert.That(differences, Has.Count.EqualTo(4));
            Assert.That(differences[0], Does.Contain("rate").And.Contain("= 2.5"));
            Assert.That(CostPlusShadowComparison.Differences(console, console), Is.Empty);
        }

        [Test]
        public void ShadowModeIsReadFromConfiguration()
        {
            Assert.That(CostPlusPollerMode.IsShadow(Config("Shadow")), Is.True);
            Assert.That(CostPlusPollerMode.IsShadow(Config("shadow")), Is.True);
            Assert.That(CostPlusPollerMode.IsShadow(Config("Live")), Is.False);
            Assert.That(CostPlusPollerMode.IsShadow(Config(null)), Is.True, "shadow is the default: only an explicit Live claims requests");
        }

        private static IConfiguration Config(string mode) =>
            new ConfigurationBuilder().AddInMemoryCollection(mode == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["CostPlus:Poller:Mode"] = mode }).Build();
    }
}
