using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AuthenticationServer.CostPlus;
using AuthenticationServer.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using AuthenticationServer.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The Reports section's Poller Shadow Run pages (#40-#45): the daily summary, the not-compared count, the day drill-down, the
    /// Shadow Comparison detail and the explanation write, through the hosted app over HTTP against the test database. Each test
    /// seeds its own rows on days of its own in the distant future, so it never meets real comparisons or another test's rows.
    /// </summary>
    public class ShadowRunReportTests
    {
        private const string Reviewer = @"WISE\reviewer";

        private SandboxCostPlus _costPlus;
        private WebApplicationFactory<Program> _factory;
        private IdentityInjector _identity;

        [SetUp]
        public void SetUp()
        {
            SandboxGuard.Require();
            _costPlus = new SandboxCostPlus();
            _identity = new IdentityInjector();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                // The app must read and write the same database the test seeds (SandboxGuard has checked it is FreightEstimator), not whatever its own Development file names.
                b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ConnectionStrings:FreightEstimator"] = System.Configuration.ConfigurationManager.ConnectionStrings["FreightEstimator"].ConnectionString
                }));
                b.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(_identity));
            });
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            _costPlus.Dispose();
            Configuration.ConnectionStringSource = null;
        }

        /// <summary>Stands in for IIS Windows authentication: puts a signed-in user on every request, or none.</summary>
        private sealed class IdentityInjector : IStartupFilter
        {
            public string UserName;

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (UserName != null)
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, UserName) }, "Test", ClaimTypes.Name, ClaimTypes.Role));
                    await nextMiddleware();
                });
                next(app);
            };
        }

        // ---- seeding ----

        private static string Day(DateTime day) => day.ToString("yyyy-MM-dd");

        /// <summary>A request on the day (at noon plus <paramref name="minute"/>) with a Shadow Comparison of the given status.</summary>
        private string Compared(DateTime day, string status, string loginId = null, DateTime? comparedAt = null, int minute = 0, object console = null, object poller = null, string differences = "Line 1 rate differs")
        {
            var request = new SandboxCostPlus.Request { ToState = "GA", ToZip = "30004", NumPackages = 2, PkgWeight = 10, DateRequested = day.Date.AddHours(12).AddMinutes(minute) };
            string id = loginId == null ? _costPlus.Add(request) : _costPlus.Add(request, loginId);
            _costPlus.MarkAnswered(id);
            bool diverged = status != "Match";
            _costPlus.AddShadow(id, diverged, console, poller, diverged ? differences : null, status == "Explained" ? "Known markup quirk" : null, comparedAt);
            return id;
        }

        private static string ProbeLogin() => CostPlusProbe.LoginIdPrefix + Guid.NewGuid().ToString("N").Substring(0, 12);

        private int ShadowIdOf(string loginId)
        {
            using (var conn = new System.Data.SqlClient.SqlConnection(Configuration.FreightEstimatorSqlConnection))
            {
                conn.Open();
                using (var cmd = new System.Data.SqlClient.SqlCommand("SELECT ShadowResultId FROM dbo.CostPlusShadowResult WHERE LoginId = @l", conn))
                {
                    cmd.Parameters.AddWithValue("@l", loginId);
                    return (int)cmd.ExecuteScalar();
                }
            }
        }

        // ---- reading pages ----

        private async Task<string> Get(string url, HttpClient client = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            HttpResponseMessage response = await (client ?? _factory.CreateClient()).GetAsync(url);
            string html = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(expected), html);
            return html;
        }

        /// <summary>The text of one cell of a summary row, e.g. Cell(html, "2091-03-04", "match").</summary>
        private static string Cell(string html, string day, string column)
        {
            Match row = Regex.Match(html, "<tr data-day=\"" + day + "\">(.*?)</tr>", RegexOptions.Singleline);
            Assert.That(row.Success, Is.True, "no summary row for " + day);
            Match cell = Regex.Match(row.Groups[1].Value, "data-col=\"" + column + "\">(.*?)</td>", RegexOptions.Singleline);
            Assert.That(cell.Success, Is.True, "no " + column + " cell on " + day);
            return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(cell.Groups[1].Value, "<.*?>", "")), @"\s+", " ").Trim();
        }

        private static string Summary(DateTime from, DateTime to) => "/Reports/ShadowRun?from=" + Day(from) + "&to=" + Day(to);

        private static string Between(string html, string id, string next) =>
            html.Substring(html.IndexOf(id, StringComparison.Ordinal), html.IndexOf(next, html.IndexOf(id, StringComparison.Ordinal), StringComparison.Ordinal) - html.IndexOf(id, StringComparison.Ordinal));

        // ====================================================================================================
        // #41: the daily summary
        // ====================================================================================================

        [Test]
        public async Task TheReportsIndexLinksToThePage()
        {
            string html = await Get("/Reports");

            Assert.That(html, Does.Contain("Poller Shadow Run").And.Contain("href=\"/Reports/ShadowRun\""));
        }

        [Test]
        public async Task TheSummaryCountsEachStatusPerDay_AndTheMatchRateLeavesExplainedOut()
        {
            DateTime day = new DateTime(2091, 3, 4);
            Compared(day, "Match");
            Compared(day, "Match");
            Compared(day, "Explained");
            Compared(day, "Unexplained");

            string html = await Get(Summary(day, day.AddDays(1)));

            Assert.That(Cell(html, Day(day), "compared"), Is.EqualTo("4"));
            Assert.That(Cell(html, Day(day), "match"), Is.EqualTo("2"));
            Assert.That(Cell(html, Day(day), "explained"), Is.EqualTo("1"));
            Assert.That(Cell(html, Day(day), "unexplained"), Is.EqualTo("1"));
            Assert.That(Cell(html, Day(day), "rate"), Is.EqualTo("50.0%"), "Match / compared; Explained is not a match");
        }

        [Test]
        public async Task ADayWithNoComparisonsIsStillListed_WithZerosAndNoRate()
        {
            DateTime day = new DateTime(2091, 3, 4);
            Compared(day, "Match");

            string html = await Get(Summary(day, day.AddDays(1)));

            Assert.That(Cell(html, Day(day.AddDays(1)), "compared"), Is.EqualTo("0"));
            Assert.That(Cell(html, Day(day.AddDays(1)), "unexplained"), Is.EqualTo("0"));
            Assert.That(Cell(html, Day(day.AddDays(1)), "rate"), Is.EqualTo("—"));
        }

        [Test]
        public async Task ProbeRequestsAreNotCounted()
        {
            DateTime day = new DateTime(2091, 4, 5);
            Compared(day, "Match");
            Compared(day, "Unexplained", ProbeLogin());

            string html = await Get(Summary(day, day));

            Assert.That(Cell(html, Day(day), "compared"), Is.EqualTo("1"));
            Assert.That(Cell(html, Day(day), "unexplained"), Is.EqualTo("0"));
        }

        [Test]
        public async Task TheLatestUnexplainedDateAndLatestComparisonTimeAreShown_ProbesLeftOut()
        {
            Compared(new DateTime(2099, 5, 4), "Unexplained", comparedAt: new DateTime(2099, 5, 4, 7, 0, 0));
            Compared(new DateTime(2099, 5, 6), "Unexplained", comparedAt: new DateTime(2099, 5, 6, 7, 8, 9));
            Compared(new DateTime(2099, 5, 7), "Match", comparedAt: new DateTime(2099, 5, 7, 10, 11, 12));
            Compared(new DateTime(2099, 12, 30), "Unexplained", ProbeLogin(), comparedAt: new DateTime(2099, 12, 30, 1, 2, 3));

            string html = await Get("/Reports/ShadowRun?from=2099-05-04&to=2099-05-04");

            Assert.That(Regex.Match(html, "id=\"latest-unexplained\">(.*?)<").Groups[1].Value, Is.EqualTo("2099-05-06"), "over all time, not just the range");
            Assert.That(Regex.Match(html, "id=\"latest-compared\">(.*?)<").Groups[1].Value, Is.EqualTo("2099-05-07 10:11:12"));
        }

        [Test]
        public async Task TheDateRangeCanBeChanged()
        {
            DateTime day = new DateTime(2091, 6, 7);

            string narrow = await Get(Summary(day, day));
            string wide = await Get(Summary(day.AddDays(-2), day.AddDays(1)));
            string byDefault = await Get("/Reports/ShadowRun");

            Assert.That(Regex.Matches(narrow, "<tr data-day=").Count, Is.EqualTo(1));
            Assert.That(Regex.Matches(wide, "<tr data-day=").Count, Is.EqualTo(4));
            Assert.That(byDefault, Does.Contain("<tr data-day=\"" + Day(DateTime.Today) + "\">"), "defaults to the recent days, ending today");
            Assert.That(Regex.Matches(byDefault, "<tr data-day=").Count, Is.EqualTo(ReportsController.ShadowRunDefaultDays));
        }

        // ====================================================================================================
        // #42: not compared
        // ====================================================================================================

        [Test]
        public async Task NotComparedCountsOnlyAnsweredUpsOnlyNonProbeRequestsWithNoComparison()
        {
            DateTime day = new DateTime(2092, 1, 2);
            SandboxCostPlus.Request At(bool ups = true, bool? ltl = false) =>
                new SandboxCostPlus.Request { ToState = "GA", ToZip = "30004", NumPackages = 1, PkgWeight = 5, RequestUps = ups, RequestLtl = ltl, DateRequested = day.AddHours(9) };

            Compared(day, "Match");                                          // compared: not counted
            string missed = _costPlus.Add(At()); _costPlus.MarkAnswered(missed);                       // counted
            string probe = _costPlus.Add(At(), ProbeLogin()); _costPlus.MarkAnswered(probe);           // probe: not counted
            string ltl = _costPlus.Add(At(ltl: true)); _costPlus.MarkAnswered(ltl);                    // LTL: not counted
            string noUps = _costPlus.Add(At(ups: false)); _costPlus.MarkAnswered(noUps);               // not a UPS request: not counted
            _costPlus.Add(At());                                                                       // unanswered: not counted

            string html = await Get(Summary(day, day));

            Assert.That(Cell(html, Day(day), "notcompared"), Is.EqualTo("1"));
            Assert.That(Cell(html, Day(day), "compared"), Is.EqualTo("1"));
        }

        [Test]
        public async Task ADayThePollerDidNotRunShowsAllItsRequestsNotComparedBesideZeroCompared()
        {
            DateTime day = new DateTime(2092, 1, 3);
            for (int i = 0; i < 3; i++)
            {
                string id = _costPlus.Add(new SandboxCostPlus.Request { ToState = "GA", ToZip = "30004", NumPackages = 1, PkgWeight = 5, DateRequested = day.AddHours(9 + i) });
                _costPlus.MarkAnswered(id);
            }

            string html = await Get(Summary(day, day));

            Assert.That(Cell(html, Day(day), "compared"), Is.EqualTo("0"));
            Assert.That(Cell(html, Day(day), "notcompared"), Is.EqualTo("3"));
        }

        [Test]
        public async Task TodaysNotComparedIsPending_AnEarlierDaysIsNot()
        {
            DateTime today = DateTime.Today;
            string before = Cell(await Get(Summary(today, today)), Day(today), "notcompared");
            int baseline = int.Parse(before.Replace("pending", "").Trim());
            string id = _costPlus.Add(new SandboxCostPlus.Request { ToState = "GA", ToZip = "30004", NumPackages = 1, PkgWeight = 5, DateRequested = DateTime.Now.AddSeconds(-1) });
            _costPlus.MarkAnswered(id);
            DateTime past = new DateTime(2092, 1, 4);
            string old = _costPlus.Add(new SandboxCostPlus.Request { ToState = "GA", ToZip = "30004", NumPackages = 1, PkgWeight = 5, DateRequested = past.AddHours(9) });
            _costPlus.MarkAnswered(old);

            string html = await Get(Summary(past, today));

            Assert.That(before, Does.EndWith("pending"));
            Assert.That(Cell(html, Day(today), "notcompared"), Is.EqualTo((baseline + 1) + " pending"));
            Assert.That(Cell(html, Day(past), "notcompared"), Is.EqualTo("1"), "an earlier day's gap is a miss, not pending");
        }

        // ====================================================================================================
        // #43: the day drill-down
        // ====================================================================================================

        [Test]
        public async Task ADayListsExactlyItsComparisons_WithRequestTimeRequesterAndStatus()
        {
            DateTime day = new DateTime(2093, 2, 3);
            string a = Compared(day, "Match", minute: 1);
            string b = Compared(day, "Unexplained", minute: 2);
            string other = Compared(day.AddDays(1), "Match");

            string html = await Get("/Reports/ShadowRunDay?date=" + Day(day));

            Assert.That(html, Does.Contain(">" + a + "<").And.Contain(">" + b + "<"));
            Assert.That(html, Does.Not.Contain(other));
            Assert.That(html, Does.Contain("12:01:00").And.Contain("12:02:00"));
            Assert.That(Regex.Matches(html, "data-col=\"status\">(Match|Unexplained)<").Count, Is.EqualTo(2));
            Assert.That(html, Does.Contain("/Reports/ShadowRunDetail/" + ShadowIdOf(a)), "each row links to its detail page");
        }

        [TestCase("Match", new[] { "m1", "m2" })]
        [TestCase("Explained", new[] { "e1" })]
        [TestCase("Unexplained", new[] { "u1", "u2", "u3" })]
        [TestCase(null, new[] { "m1", "m2", "e1", "u1", "u2", "u3" })]
        [TestCase("nonsense", new[] { "m1", "m2", "e1", "u1", "u2", "u3" })]
        public async Task TheStatusFilterNarrowsTheList(string status, string[] expected)
        {
            DateTime day = new DateTime(2093, 2, 4);
            var ids = new Dictionary<string, string>
            {
                ["m1"] = Compared(day, "Match", minute: 1), ["m2"] = Compared(day, "Match", minute: 2),
                ["e1"] = Compared(day, "Explained", minute: 3),
                ["u1"] = Compared(day, "Unexplained", minute: 4), ["u2"] = Compared(day, "Unexplained", minute: 5), ["u3"] = Compared(day, "Unexplained", minute: 6)
            };

            string html = await Get("/Reports/ShadowRunDay?date=" + Day(day) + (status == null ? "" : "&status=" + status));

            foreach (var entry in ids)
            {
                if (expected.Contains(entry.Key)) Assert.That(html, Does.Contain(">" + entry.Value + "<"), entry.Key);
                else Assert.That(html, Does.Not.Contain(entry.Value), entry.Key);
            }
        }

        [Test]
        public async Task UnexplainedIsOneClickAwayOnTheListAndOnTheSummary()
        {
            DateTime day = new DateTime(2093, 2, 5);
            Compared(day, "Unexplained");

            string list = await Get("/Reports/ShadowRunDay?date=" + Day(day));
            string summary = await Get(Summary(day, day));

            Assert.That(list, Does.Contain("ShadowRunDay?date=" + Day(day) + "&amp;status=Unexplained"));
            Assert.That(summary, Does.Contain("ShadowRunDay?date=" + Day(day) + "&amp;status=Unexplained"), "the Unexplained count links straight to its list");
            Assert.That(summary, Does.Contain("ShadowRunDay?date=" + Day(day) + "\""), "the day links to its whole list");
        }

        // ====================================================================================================
        // #44: the detail page
        // ====================================================================================================

        private static object Result(string errorCode = null, string errorText = null, bool? residential = null, string zip = null, params (string Service, decimal Rate, string Note)[] lines) =>
            new
            {
                Lines = lines.Select(l => new { l.Service, l.Rate, l.Note }).ToArray(),
                ErrorCode = errorCode, ErrorDescription = errorText, ClassifiedResidential = residential, SubstitutedZip = zip
            };

        private static bool Differs(string html, string rowAttribute, string column = null)
        {
            Match row = Regex.Match(html, "<tr " + rowAttribute + ">(.*?)</tr>", RegexOptions.Singleline);
            Assert.That(row.Success, Is.True, "no row " + rowAttribute);
            return column == null
                ? Regex.IsMatch(row.Groups[1].Value, "class=\"[^\"]*\\bdiff\\b")
                : Regex.IsMatch(row.Groups[1].Value, "<td class=\"[^\"]*\\bdiff\\b[^\"]*\" data-col=\"" + column + "\"");
        }

        [Test]
        public async Task ARateDivergenceHighlightsOnlyTheDifferingCells_AndShowsTheRequestInputs()
        {
            DateTime day = new DateTime(2094, 3, 4);
            var console = Result(lines: new[] { ("UPSGround", 10.00m, "A"), ("UPS2DayAir", 20.00m, "B") });
            var poller = Result(lines: new[] { ("UPSGround", 10.00m, "A"), ("UPS2DayAir", 21.50m, "B") });
            string id = Compared(day, "Unexplained", console: console, poller: poller, differences: "Line 2 (UPS2DayAir) rate: console app 20.00, poller 21.50 (poller - console app = 1.50)");

            string html = await Get("/Reports/ShadowRunDetail/" + ShadowIdOf(id));

            Assert.That(Differs(html, "data-line=\"1\""), Is.False, "a matching line is not highlighted");
            Assert.That(Differs(html, "data-line=\"2\"", "console-rate"), Is.True);
            Assert.That(Differs(html, "data-line=\"2\"", "poller-rate"), Is.True);
            Assert.That(Differs(html, "data-line=\"2\"", "console-service"), Is.False, "only the cell that differs");
            Assert.That(Differs(html, "data-line=\"2\"", "console-note"), Is.False);
            Assert.That(Differs(html, "data-field=\"error-code\""), Is.False);
            Assert.That(html, Does.Contain("21.50").And.Contain("poller - console app = 1.50"));
            Assert.That(Regex.Match(html, "<tr data-line=\"1\">(.*?)</tr>", RegexOptions.Singleline).Value, Does.Contain("UPSGround"));
            // the request's own inputs
            Assert.That(html, Does.Contain("id=\"request-inputs\"").And.Contain("<th>ToZip</th><td>30004</td>").And.Contain("<th>NumPackages</th><td>2</td>").And.Contain("<th>LoginId</th><td>" + id + "</td>"));
        }

        [Test]
        public async Task ErrorClassifiedResidentialAndSubstitutedZipDivergencesAreVisible()
        {
            DateTime day = new DateTime(2094, 3, 5);
            var console = Result(residential: true, zip: "30004", lines: new[] { ("UPSGround", 10.00m, "") });
            var poller = Result(errorCode: "E42", errorText: "Address rejected", residential: false, zip: "30005", lines: new[] { ("UPSGround", 10.00m, "") });
            string id = Compared(day, "Unexplained", console: console, poller: poller,
                differences: "Error code: console app (none), poller \"E42\"\r\nClassifiedResidential: console app \"True\", poller \"False\"\r\nSubstitutedZip: console app \"30004\", poller \"30005\"");

            string html = await Get("/Reports/ShadowRunDetail/" + ShadowIdOf(id));

            Assert.That(Differs(html, "data-field=\"error-code\""), Is.True);
            Assert.That(Differs(html, "data-field=\"error-text\""), Is.True);
            Assert.That(Differs(html, "data-field=\"classified-residential\""), Is.True);
            Assert.That(Differs(html, "data-field=\"substituted-zip\""), Is.True);
            Assert.That(Differs(html, "data-line=\"1\""), Is.False);
            Assert.That(html, Does.Contain("E42").And.Contain("Address rejected").And.Contain("30005"));
            Assert.That(Regex.Matches(Between(html, "id=\"differences\"", "</ul>"), "<li>").Count, Is.EqualTo(3), "each stored difference sentence is shown");
        }

        [Test]
        public async Task APurgedRequestRowIsAnExplicitNote_NotBlanksOrAnError()
        {
            DateTime day = new DateTime(2094, 3, 6);
            string id = Compared(day, "Unexplained", console: Result(lines: new[] { ("UPSGround", 10.00m, "") }), poller: Result(lines: new[] { ("UPSGround", 11.00m, "") }));
            int shadowId = ShadowIdOf(id);
            _costPlus.PurgeRequest(id);

            string html = await Get("/Reports/ShadowRunDetail/" + shadowId);

            Assert.That(html, Does.Contain("id=\"request-purged\"").And.Contain("has been purged"));
            Assert.That(html, Does.Not.Contain("id=\"request-inputs\""));
            Assert.That(html, Does.Contain("11.00"), "the comparison itself still shows");
        }

        [Test]
        public async Task AnUnknownComparisonIsNotFound() =>
            await Get("/Reports/ShadowRunDetail/2000000000", expected: HttpStatusCode.NotFound);

        // ====================================================================================================
        // #45: record an explanation
        // ====================================================================================================

        /// <summary>Posts the detail page's form as the signed-in reviewer would, with the anti-forgery token the page gave.</summary>
        private async Task<HttpResponseMessage> Explain(HttpClient client, int shadowId, string explanation)
        {
            string page = await Get("/Reports/ShadowRunDetail/" + shadowId, client);
            string token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.That(token, Is.Not.Empty, "the form carries an anti-forgery token");
            return await client.PostAsync("/Reports/ShadowRunExplain", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = shadowId.ToString(), ["explanation"] = explanation, ["__RequestVerificationToken"] = token
            }));
        }

        private string UnexplainedOn(DateTime day) =>
            Compared(day, "Unexplained", console: Result(lines: new[] { ("UPSGround", 10.00m, "") }), poller: Result(lines: new[] { ("UPSGround", 12.00m, "") }));

        [Test]
        public async Task SavingAnExplanationMakesItExplained_RecordsWhoAndWhen_AndTheSummaryFollows()
        {
            DateTime day = new DateTime(2095, 4, 5);
            string id = UnexplainedOn(day);
            _identity.UserName = Reviewer;
            DateTime before = DateTime.Now.AddMinutes(-1);

            HttpResponseMessage response = await Explain(_factory.CreateClient(), ShadowIdOf(id), "  Known markup quirk, see docs/issues/0001  ");
            string page = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), page);
            var row = _costPlus.ExplainedOf(id);
            Assert.That(row.Status, Is.EqualTo("Explained"));
            Assert.That(row.Explanation, Is.EqualTo("Known markup quirk, see docs/issues/0001"), "trimmed");
            Assert.That(row.ExplainedBy, Is.EqualTo(Reviewer));
            Assert.That(row.ExplainedAt, Is.GreaterThan(before).And.LessThan(DateTime.Now.AddMinutes(1)));
            Assert.That(Regex.Match(page, "id=\"status\">(.*?)<").Groups[1].Value, Is.EqualTo("Explained"), "success returns to the detail page showing the new status");
            string summary = await Get(Summary(day, day));
            Assert.That(Cell(summary, Day(day), "explained"), Is.EqualTo("1"));
            Assert.That(Cell(summary, Day(day), "unexplained"), Is.EqualTo("0"));
        }

        [Test]
        public async Task AnExplanationCanBeEdited()
        {
            string id = UnexplainedOn(new DateTime(2095, 4, 6));
            _identity.UserName = Reviewer;
            HttpClient client = _factory.CreateClient();
            await Explain(client, ShadowIdOf(id), "first reason");

            await Explain(client, ShadowIdOf(id), "better reason");

            var row = _costPlus.ExplainedOf(id);
            Assert.That(row.Explanation, Is.EqualTo("better reason"));
            Assert.That(row.Status, Is.EqualTo("Explained"));
            Assert.That(row.ExplainedBy, Is.EqualTo(Reviewer));
        }

        [TestCase("")]
        [TestCase("   \r\n  ")]
        public async Task SavingNothingOnAnExplainedComparisonClearsItAndBothColumns(string empty)
        {
            string id = UnexplainedOn(new DateTime(2095, 4, 7));
            _identity.UserName = Reviewer;
            HttpClient client = _factory.CreateClient();
            await Explain(client, ShadowIdOf(id), "a reason");

            HttpResponseMessage response = await Explain(client, ShadowIdOf(id), empty);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var row = _costPlus.ExplainedOf(id);
            Assert.That(row.Status, Is.EqualTo("Unexplained"));
            Assert.That(row.Explanation, Is.Null);
            Assert.That(row.ExplainedBy, Is.Null);
            Assert.That(row.ExplainedAt, Is.Null);
        }

        [TestCase("")]
        [TestCase("   \t  ")]
        public async Task AnEmptyOrWhitespaceExplanationIsRejectedWithAMessage_AndNothingIsWritten(string empty)
        {
            string id = UnexplainedOn(new DateTime(2095, 4, 8));
            _identity.UserName = Reviewer;

            HttpResponseMessage response = await Explain(_factory.CreateClient(), ShadowIdOf(id), empty);
            string page = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(page, Does.Contain("id=\"explain-error\"").And.Contain("Enter an explanation"));
            var row = _costPlus.ExplainedOf(id);
            Assert.That(row.Status, Is.EqualTo("Unexplained"));
            Assert.That(row.ExplainedBy, Is.Null);
            Assert.That(row.ExplainedAt, Is.Null);
        }

        [Test]
        public async Task AnExplanationOver1000CharactersIsRejected_Exactly1000IsKept()
        {
            string id = UnexplainedOn(new DateTime(2095, 4, 9));
            _identity.UserName = Reviewer;
            HttpClient client = _factory.CreateClient();

            HttpResponseMessage tooLong = await Explain(client, ShadowIdOf(id), new string('x', 1001));
            string page = await tooLong.Content.ReadAsStringAsync();

            Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(page, Does.Contain("id=\"explain-error\"").And.Contain("1000"));
            Assert.That(_costPlus.ExplainedOf(id).Status, Is.EqualTo("Unexplained"));

            HttpResponseMessage exact = await Explain(client, ShadowIdOf(id), new string('y', 1000));

            Assert.That(exact.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_costPlus.ExplainedOf(id).Explanation, Has.Length.EqualTo(1000));
        }

        [Test]
        public async Task ASaveWithNoAuthenticatedIdentityIsRefused_AndNothingIsWritten()
        {
            string id = UnexplainedOn(new DateTime(2095, 4, 10));
            _identity.UserName = null;

            HttpResponseMessage response = await _factory.CreateClient().PostAsync("/Reports/ShadowRunExplain",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = ShadowIdOf(id).ToString(), ["explanation"] = "sneaky" }));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            var row = _costPlus.ExplainedOf(id);
            Assert.That(row.Status, Is.EqualTo("Unexplained"));
            Assert.That(row.Explanation, Is.Null);
            Assert.That(row.ExplainedBy, Is.Null);
        }

        [Test]
        public async Task WithoutASignedInUserThePageIsReadOnly()
        {
            string id = UnexplainedOn(new DateTime(2095, 4, 11));
            _identity.UserName = null;

            string html = await Get("/Reports/ShadowRunDetail/" + ShadowIdOf(id));

            Assert.That(html, Does.Contain("id=\"explain-readonly\"").And.Not.Contain("id=\"explain-form\""));
        }

        [Test]
        public async Task AMatchShowsNoEditBox_AndCannotBeExplained()
        {
            string match = Compared(new DateTime(2095, 4, 12), "Match");
            string other = UnexplainedOn(new DateTime(2095, 4, 12));
            _identity.UserName = Reviewer;
            HttpClient client = _factory.CreateClient();

            string html = await Get("/Reports/ShadowRunDetail/" + ShadowIdOf(match), client);
            HttpResponseMessage response = await Explain(client, ShadowIdOf(other), "valid reason on another comparison");   // a token to post with
            string token = Regex.Match(await Get("/Reports/ShadowRunDetail/" + ShadowIdOf(other), client), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            HttpResponseMessage forced = await client.PostAsync("/Reports/ShadowRunExplain", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = ShadowIdOf(match).ToString(), ["explanation"] = "forced", ["__RequestVerificationToken"] = token
            }));

            Assert.That(html, Does.Not.Contain("explain-form").And.Not.Contain("<textarea"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(forced.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            var row = _costPlus.ExplainedOf(match);
            Assert.That(row.Status, Is.EqualTo("Match"));
            Assert.That(row.Explanation, Is.Null);
            Assert.That(row.ExplainedBy, Is.Null);
        }
    }
}
