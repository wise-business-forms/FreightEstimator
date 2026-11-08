using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using FreightEstimator.QuoteTests.Replay;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The replay harness (#26) end to end against SANDBOX: stored calls are put in the tables the harness reads, replayed
    /// through the legacy formula and the unified module with the carriers faked, and compared. The stored calls here are
    /// the recorded carrier responses from #15, under the same known Plant Charges. No test reaches a carrier: an
    /// unanswered carrier call would fail the replay and show up in <see cref="ReplayOutcome.Skipped"/>.
    /// </summary>
    public class ReplayHarnessTests
    {
        private readonly List<int> _requestRows = new List<int>();
        private readonly List<int> _ltlRows = new List<int>();

        [SetUp]
        public void SetUp() => SandboxGuard.Require();

        [TearDown]
        public void TearDown()
        {
            using (var conn = Open())
            {
                foreach (int id in _requestRows) Exec(conn, "DELETE FROM dbo.Requests_Rate WHERE RequestId = " + id);
                foreach (int id in _ltlRows) Exec(conn, "DELETE FROM dbo.Log_LTL WHERE LTLRequestId = " + id);
            }
        }

        [Test]
        public void StoredParcelResponsesAreComparedPerService_AndOnlyTheKnownCauseDiverges()
        {
            QuoteCase c = QuoteCase.All("parcel").First(x => x.Packages >= 2);
            int id = StoreUps(c, groundFreight: false);

            ReplayOutcome outcome = Replay(c.Plant, new[] { id }, null);

            Assert.That(outcome.Skipped, Is.Empty);
            Assert.That(outcome.Rows, Is.Not.Empty);
            Assert.That(outcome.Rows.Select(r => r.CallSite), Is.All.EqualTo("GRID1Single"));
            Assert.That(outcome.Rows.Select(r => r.Source), Is.All.EqualTo("Requests_Rate " + id));
            // KnownSchedule gives the UPS row real markups, so a non-CWT service differs by exactly its markup (#14);
            // a CWT service is priced the same way by both formulas.
            Assert.That(outcome.Rows.Where(r => r.IsCwtEligible).Select(r => r.Category), Is.All.AnyOf(DeltaCategory.Identical, DeltaCategory.RoundingNoise));
            Assert.That(outcome.Rows.Where(r => !r.IsCwtEligible).Select(r => r.Category), Is.All.EqualTo(DeltaCategory.KnownCause));
            Assert.That(ReplayReport.Build(outcome.Rows).UnexplainedCount("GRID1Single"), Is.Zero);
        }

        [Test]
        public void ARecordedNonCwtCaseIsReportedWithItsMarkupAsTheDelta()
        {
            QuoteCase c = QuoteCase.All("parcel").First(x => x.Packages == 1);
            int id = StoreUps(c, groundFreight: false);

            ReplayOutcome outcome = Replay(c.Plant, new[] { id }, null);

            ReplayRow ground = outcome.Rows.Single(r => r.Service == "UPSGround");
            Assert.That(ground.IsCwtEligible, Is.False);
            Assert.That(ground.MarkupPercent, Is.EqualTo(5m), "KnownSchedule's UPS Ground markup");
            Assert.That(ground.Delta, Is.EqualTo(ground.CarrierRate * 5m / 100m).Within(0.01m));
        }

        [Test]
        public void StoredGroundFreightResponsesAgreeBetweenTheFormulas()
        {
            QuoteCase c = QuoteCase.All("groundfreight").First();
            int id = StoreUps(c, groundFreight: true);

            ReplayOutcome outcome = Replay(c.Plant, new[] { id }, null);

            Assert.That(outcome.Skipped, Is.Empty);
            ReplayRow row = outcome.Rows.Single();
            Assert.That(row.CallSite, Is.EqualTo("GRID2"));
            Assert.That(row.Service, Is.EqualTo("UPSGroundFreight"));
            Assert.That(row.Category, Is.AnyOf(DeltaCategory.Identical, DeltaCategory.RoundingNoise));
        }

        [TestCaseSource(typeof(ReplayHarnessTests), nameof(LtlCases))]
        public void StoredLogisticsPartnerResponsesAgreeBetweenTheFormulas_OptionByOption(QuoteCase c)
        {
            int id = StoreLtl(c);

            ReplayOutcome outcome = Replay(c.Plant, null, new[] { id });

            Assert.That(outcome.Skipped, Is.Empty);
            Assert.That(outcome.Rows, Is.Not.Empty);
            Assert.That(outcome.Rows.Select(r => r.CallSite), Is.All.EqualTo("LTL"));
            Assert.That(outcome.Rows.Select(r => r.Category), Is.All.AnyOf(DeltaCategory.Identical, DeltaCategory.RoundingNoise));
        }

        public static IEnumerable<TestCaseData> LtlCases() =>
            QuoteCase.All("ltl").Select(c => new TestCaseData(c).SetName("ltl " + c.CaseId));

        [Test]
        public void AReplayLeavesNoRowsBehindInTheTablesTheLegacyPathLogsTo()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            int id = StoreUps(c, groundFreight: false);
            int before = Count("dbo.Requests_Rate") + Count("dbo.QuoteServiceCalculation") + Count("dbo.CalculatedRateShadowLog") + Count("dbo.Log_LTL");

            Replay(c.Plant, new[] { id }, null);

            Assert.That(Count("dbo.Requests_Rate") + Count("dbo.QuoteServiceCalculation") + Count("dbo.CalculatedRateShadowLog") + Count("dbo.Log_LTL"), Is.EqualTo(before));
        }

        [Test]
        public void ARowThatCannotBeReplayedIsReportedSkippedWithItsReason_NotDropped()
        {
            QuoteCase c = QuoteCase.All("parcel").First();
            int id = StoreRaw("{\"RateRequest\":{\"Shipment\":{\"ShipFrom\":{\"Address\":{\"City\":\"Nowhere\",\"StateProvinceCode\":\"ZZ\"}},\"ShipTo\":{\"Address\":{}},\"Package\":[{\"PackageWeight\":{\"Weight\":\"1\"}}]}}}", c.ResponseText);

            ReplayOutcome outcome = Replay(c.Plant, new[] { id }, null);

            Assert.That(outcome.Rows, Is.Empty);
            Assert.That(outcome.Skipped.Single(), Does.Contain("Requests_Rate " + id).And.Contain("not an active plant"));
        }

        /// <summary>
        /// Replays the newest stored calls in SANDBOX and writes the report. Run it by name once real responses have been
        /// copied in (a human step: they hold customer addresses). QUOTETESTS_REPLAY_SAMPLE is how many of each kind
        /// (default 500); QUOTETESTS_REPLAY_REPORT is where the Markdown goes (default: the test's output).
        /// </summary>
        [Test, Explicit("Replays whatever stored responses SANDBOX holds. See docs/replay-harness.md.")]
        public void ReplayStoredResponsesAndWriteTheReport()
        {
            int sample = int.TryParse(Environment.GetEnvironmentVariable("QUOTETESTS_REPLAY_SAMPLE"), out int n) ? n : 500;

            StoredResponses stored = StoredResponses.Read(sample);
            ReplayOutcome outcome = ReplayRunner.Run(stored);
            string markdown = ReplayReport.Build(outcome.Rows).ToMarkdown()
                + Environment.NewLine + "## Not replayed" + Environment.NewLine + Environment.NewLine
                + (outcome.Skipped.Count == 0 ? "None." : string.Join(Environment.NewLine, outcome.Skipped.Take(50).Select(s => "* " + s)) + (outcome.Skipped.Count > 50 ? Environment.NewLine + "* ... and " + (outcome.Skipped.Count - 50) + " more" : ""))
                + Environment.NewLine;

            string path = Environment.GetEnvironmentVariable("QUOTETESTS_REPLAY_REPORT");
            if (string.IsNullOrWhiteSpace(path)) TestContext.Out.WriteLine(markdown);
            else File.WriteAllText(path, markdown);

            Assert.That(stored.Ups.Count + stored.Ltl.Count + stored.Skipped.Count, Is.GreaterThan(0), "SANDBOX holds no stored carrier calls to replay");
        }

        // ---- helpers --------------------------------------------------------------------------------------------

        private static ReplayOutcome Replay(string plant, int[] upsIds, int[] ltlIds)
        {
            using (new SandboxPlantCharges(plant, SandboxPlantCharges.KnownSchedule()))
            {
                return ReplayRunner.Run(StoredResponses.Read(10, upsIds ?? new int[0], ltlIds ?? new int[0]));
            }
        }

        private int StoreUps(QuoteCase c, bool groundFreight)
        {
            var (city, state) = PlantLocation(c.Plant);
            var packages = new List<string>();
            for (int i = 0; i < c.Packages - 1; i++) packages.Add(PackageJson(c.PackageWeight));
            packages.Add(PackageJson(c.LastPackageWeight > 0 ? c.LastPackageWeight : c.PackageWeight));

            string request = "{\"RateRequest\":{\"Shipment\":{"
                + "\"ShipFrom\":{\"Address\":{\"City\":\"" + city + "\",\"StateProvinceCode\":\"" + state + "\"}},"
                + "\"ShipTo\":{\"Address\":{\"AddressLine\":[\"1 Main St\",\"\",\"\"],\"City\":\"Anytown\",\"StateProvinceCode\":\"" + c.ShipToState + "\",\"PostalCode\":\"" + c.ShipToZip + "\",\"CountryCode\":\"US\"}},"
                + (groundFreight ? "\"FRSPaymentInformation\":{\"Type\":{\"Code\":\"01\"}}," : "\"PaymentDetails\":{},")
                + "\"Package\":[" + string.Join(",", packages) + "]}}}";
            return StoreRaw(request, c.ResponseText);
        }

        private static string PackageJson(float weight) =>
            "{\"PackageWeight\":{\"UnitOfMeasurement\":{\"Code\":\"LBS\"},\"Weight\":\"" + weight.ToString(CultureInfo.InvariantCulture) + "\"}}";

        private int StoreRaw(string requestJson, string responseJson)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("INSERT dbo.Requests_Rate (UserName, Address, City, State, Zip, Country, RequestDate, RequestXml, ResponseXml) OUTPUT INSERTED.RequestId VALUES ('replay-test', '', '', '', '', 'US', GETDATE(), @q, @s)", conn))
            {
                cmd.Parameters.AddWithValue("@q", requestJson);
                cmd.Parameters.AddWithValue("@s", responseJson);
                int id = (int)cmd.ExecuteScalar();
                _requestRows.Add(id);
                return id;
            }
        }

        private int StoreLtl(QuoteCase c)
        {
            var items = new System.Text.StringBuilder();
            for (int i = 0; i < c.Packages - 1; i++) items.Append(LtlItem(c.FreightClass, c.PackageWeight));
            items.Append(LtlItem(c.FreightClass, c.LastPackageWeight > 0 ? c.LastPackageWeight : c.PackageWeight));

            string xml = "<?xml version=\"1.0\"?><service-request><data><RateRequest><Items>" + items + "</Items><Events>"
                + "<Event sequence=\"1\" type=\"Pickup\" date=\"11/03/2026 00:00\"><Location><City>X</City><State>PA</State><Zip>15201</Zip><Country>USA</Country></Location></Event>"
                + "<Event sequence=\"2\" type=\"Drop\" date=\"11/03/2026 00:00\"><Location><City>PITTSBURGH</City><State>" + c.ShipToState + "</State><Zip>" + c.ShipToZip + "</Zip><Country>USA</Country></Location></Event>"
                + "</Events></RateRequest></data></service-request>";

            using (var conn = Open())
            using (var cmd = new SqlCommand("INSERT dbo.Log_LTL (PlantCode, UserName, FullRequest, FullResults, XmlResponse, RequestDate) OUTPUT INSERTED.LTLRequestId VALUES (@p, 'replay-test', @q, @s, @s, GETDATE())", conn))
            {
                cmd.Parameters.AddWithValue("@p", c.Plant);
                cmd.Parameters.AddWithValue("@q", "userid=u&password=p&request=" + xml);
                cmd.Parameters.AddWithValue("@s", c.ResponseText);
                int id = (int)cmd.ExecuteScalar();
                _ltlRows.Add(id);
                return id;
            }
        }

        private static string LtlItem(int freightClass, float weight) =>
            "<Item sequence=\"1\" freightClass=\"" + freightClass + "\"><Weight units=\"lb\">" + weight.ToString(CultureInfo.InvariantCulture) + "</Weight></Item>";

        private static (string City, string State) PlantLocation(string plant)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT TOP 1 City, State FROM dbo.Plants WHERE PlantCode = @p AND Active = 'Y'", conn))
            {
                cmd.Parameters.AddWithValue("@p", plant);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) Assert.Inconclusive("SANDBOX has no active plant " + plant);
                    return (r.GetString(0).Trim(), r.GetString(1).Trim());
                }
            }
        }

        private static int Count(string table)
        {
            using (var conn = Open())
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM " + table, conn)) return (int)cmd.ExecuteScalar();
        }

        private static void Exec(SqlConnection conn, string sql)
        {
            using (var cmd = new SqlCommand(sql, conn)) cmd.ExecuteNonQuery();
        }

        private static SqlConnection Open()
        {
            var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["FreightEstimator"].ConnectionString);
            conn.Open();
            return conn;
        }
    }
}
