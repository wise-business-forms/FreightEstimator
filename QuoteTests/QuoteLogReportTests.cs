using System.Net;
using System.Text.RegularExpressions;
using AuthenticationServer.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The Quote Log (#53, #54, ADR 0007): the Reports page that lists the Quotes production has answered, read through the
    /// UpsRateReadOnly connection. Through the hosted app over HTTP. These tests never touch UPSRate. Production UPSRate calls its
    /// table dbo.Requests_Rate and the report reads it by that name, but the FreightEstimator database renamed its own copy to
    /// UpsRateRequestLog (Database/008). So the fixture creates a stand-in dbo.Requests_Rate (production's layout) in the
    /// FreightEstimator test database if it is missing, each test seeds rows there (on days of its own in the distant future) and
    /// UpsRateReadOnly is pointed at that database. SandboxGuard has checked it is FreightEstimator.
    /// </summary>
    public class QuoteLogReportTests
    {
        private WebApplicationFactory<Program> _factory;
        private readonly List<int> _seeded = new List<int>();
        private string _connectionString;

        [SetUp]
        public void SetUp()
        {
            SandboxGuard.Require();
            _connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["FreightEstimator"].ConnectionString;
            SandboxGuard.RequireNotUpsRate(_connectionString);
            EnsureProductionShapedTable();
            _factory = HostWith(_connectionString);
        }

        /// <summary>The stand-in for production's dbo.Requests_Rate, in the FreightEstimator test database only.</summary>
        private void EnsureProductionShapedTable()
        {
            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new System.Data.SqlClient.SqlCommand(
                    "IF OBJECT_ID('dbo.Requests_Rate', 'U') IS NULL CREATE TABLE dbo.Requests_Rate (" +
                    "RequestId INT IDENTITY(1,1) NOT NULL, UserName VARCHAR(50) NULL, TargetUrl VARCHAR(200) NULL, Address VARCHAR(200) NULL, " +
                    "City VARCHAR(200) NULL, State VARCHAR(50) NULL, Zip VARCHAR(50) NULL, Country VARCHAR(2) NULL, RequestDate DATETIME NULL, " +
                    "RequestXml NVARCHAR(MAX) NULL, ResponseXml NVARCHAR(MAX) NULL, CONSTRAINT PK_Requests_Rate PRIMARY KEY (RequestId))", conn))
                    cmd.ExecuteNonQuery();
            }
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            {
                conn.Open();
                foreach (int id in _seeded)
                    using (var cmd = new System.Data.SqlClient.SqlCommand("DELETE dbo.Requests_Rate WHERE RequestId = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
            }
            Configuration.ConnectionStringSource = null;
        }

        /// <summary>The app, reading both the main connection and (when given) the Quote Log's from the test database.</summary>
        private WebApplicationFactory<Program> HostWith(string quoteLogConnection) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = new Dictionary<string, string> { ["ConnectionStrings:FreightEstimator"] = _connectionString };
                    if (quoteLogConnection != null) settings["ConnectionStrings:UpsRateReadOnly"] = quoteLogConnection;
                    config.AddInMemoryCollection(settings);
                });
            });

        // ---- seeding ----

        private static string Day(DateTime day) => day.ToString("yyyy-MM-dd");

        private static string RequestJson(string toState = "TN", string toZip = "37201", string street = "1 Main St") =>
            "{\"RateRequest\":{\"Shipment\":{\"ShipFrom\":{\"Address\":{\"City\":\"Alpharetta\",\"StateProvinceCode\":\"GA\"}}," +
            "\"ShipTo\":{\"Address\":{\"AddressLine\":[\"" + street + "\"],\"City\":\"Nashville\",\"StateProvinceCode\":\"" + toState + "\",\"PostalCode\":\"" + toZip + "\"}}," +
            "\"Package\":[{\"PackageWeight\":{\"Weight\":\"10\"}}]}}}";

        private static string Service(string code, string published, string negotiated = null) =>
            "{\"Service\":{\"Code\":\"" + code + "\"},\"TotalCharges\":{\"MonetaryValue\":\"" + published + "\"}" +
            (negotiated == null ? "" : ",\"NegotiatedRateCharges\":{\"TotalCharge\":{\"MonetaryValue\":\"" + negotiated + "\"}}") + "}";

        private static string Rated(params string[] services) => "{\"RateResponse\":{\"RatedShipment\":[" + string.Join(",", services) + "]}}";

        /// <summary>One production-shaped UPS Quote in Requests_Rate; returns its RequestId.</summary>
        private int Add(DateTime when, string userName = null, string requestJson = null, string responseJson = null, string address = "1 Main St")
        {
            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new System.Data.SqlClient.SqlCommand(
                    "INSERT dbo.Requests_Rate (UserName, TargetUrl, Address, City, State, Zip, Country, RequestDate, RequestXml, ResponseXml) " +
                    "VALUES (@u, 'test', @a, 'Nashville', 'TN', '37201', 'US', @d, @req, @res); SELECT CAST(SCOPE_IDENTITY() AS INT)", conn))
                {
                    cmd.Parameters.AddWithValue("@u", (object)userName ?? "ql-user");
                    cmd.Parameters.AddWithValue("@a", address);
                    cmd.Parameters.AddWithValue("@d", when);
                    cmd.Parameters.AddWithValue("@req", requestJson ?? RequestJson());
                    cmd.Parameters.AddWithValue("@res", (object)responseJson ?? Rated(Service("03", "12.34", "10.00"), Service("01", "50.00")));
                    int id = (int)cmd.ExecuteScalar();
                    _seeded.Add(id);
                    return id;
                }
            }
        }

        private int CountOf(string table)
        {
            using (var conn = new System.Data.SqlClient.SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new System.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM dbo." + table, conn))
                    return (int)cmd.ExecuteScalar();
            }
        }

        // ---- reading pages ----

        private async Task<string> Get(string url, WebApplicationFactory<Program> factory = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            HttpResponseMessage response = await (factory ?? _factory).CreateClient().GetAsync(url);
            string html = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(expected), html);
            return html;
        }

        private static string Log(DateTime from, DateTime to, int page = 1) => "/Reports/QuoteLog?from=" + Day(from) + "&to=" + Day(to) + "&page=" + page;

        private static List<int> RowIds(string html) =>
            Regex.Matches(html, "<tr data-request=\"(\\d+)\"").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();

        /// <summary>The text of one cell of a log row, e.g. Cell(html, id, "outcome").</summary>
        private static string Cell(string html, int id, string column)
        {
            Match row = Regex.Match(html, "<tr data-request=\"" + id + "\">(.*?)</tr>", RegexOptions.Singleline);
            Assert.That(row.Success, Is.True, "no row for request " + id);
            Match cell = Regex.Match(row.Groups[1].Value, "data-col=\"" + column + "\"[^>]*>(.*?)</td>", RegexOptions.Singleline);
            Assert.That(cell.Success, Is.True, "no " + column + " cell on request " + id);
            return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(cell.Groups[1].Value, "<.*?>", "")), @"\s+", " ").Trim();
        }

        /// <summary>Every row of a range, following the pages: the test database holds other tests' recent rows, so a seeded one may not be on page 1.</summary>
        private async Task<List<int>> RowIdsAcrossPages(string baseUrl)
        {
            var ids = new List<int>();
            string html = await Get(baseUrl + "?page=1");
            int pages = int.Parse(Regex.Match(html, @"Page \d+ of (\d+)").Groups[1].Value);
            ids.AddRange(RowIds(html));
            for (int page = 2; page <= pages; page++) ids.AddRange(RowIds(await Get(baseUrl + "?page=" + page)));
            return ids;
        }

        private static string Total(string html)
        {
            Match m = Regex.Match(html, "id=\"total\">(.*?)</", RegexOptions.Singleline);
            Assert.That(m.Success, Is.True, "no total on the page");
            return m.Groups[1].Value.Trim();
        }

        // ====================================================================================================
        // #54: the log opens on production UPS Quotes
        // ====================================================================================================

        [Test]
        public async Task TheReportsIndexLinksToTheQuoteLog()
        {
            string html = await Get("/Reports");

            Assert.That(html, Does.Contain("Quote Log").And.Contain("href=\"/Reports/QuoteLog\""));
        }

        [Test]
        public async Task QuotesAreListedNewestFirst_WithWhenRequesterShipToServicesAndOutcome()
        {
            DateTime day = new DateTime(2091, 3, 4);
            int early = Add(day.AddHours(9), "ql-ann", RequestJson("TN", "37201"), Rated(Service("03", "12.34")));
            int late = Add(day.AddHours(15).AddMinutes(5), "ql-bob", RequestJson("OH", "45202"), Rated(Service("03", "9.00"), Service("01", "40.00"), Service("02", "30.00")));

            string html = await Get(Log(day, day));

            Assert.That(RowIds(html), Is.EqualTo(new[] { late, early }));
            Assert.That(Cell(html, late, "when"), Is.EqualTo("2091-03-04 15:05"));
            Assert.That(Cell(html, late, "requestedby"), Is.EqualTo("ql-bob"));
            Assert.That(Cell(html, late, "shipto"), Is.EqualTo("OH 45202"));
            Assert.That(Cell(html, late, "services"), Is.EqualTo("3"));
            Assert.That(Cell(html, late, "outcome"), Is.EqualTo("Rated"));
            Assert.That(Cell(html, early, "services"), Is.EqualTo("1"));
        }

        [Test]
        public async Task AResponseWithNoServicesIsEmpty_AndAUpsErrorOrUnreadableResponseIsAnError()
        {
            DateTime day = new DateTime(2091, 3, 5);
            int empty = Add(day.AddHours(9), responseJson: "{\"RateResponse\":{}}");
            int blank = Add(day.AddHours(10), responseJson: "");
            int error = Add(day.AddHours(11), responseJson: "{\"response\":{\"errors\":[{\"code\":\"111210\",\"message\":\"The requested service is unavailable between the selected locations.\"}]}}");
            int garbled = Add(day.AddHours(12), responseJson: "<html>gateway timeout");

            string html = await Get(Log(day, day));

            Assert.That(Cell(html, empty, "outcome"), Is.EqualTo("Empty"));
            Assert.That(Cell(html, blank, "outcome"), Is.EqualTo("Empty"));
            Assert.That(Cell(html, error, "outcome"), Does.StartWith("Error").And.Contain("111210"));
            Assert.That(Cell(html, garbled, "outcome"), Does.StartWith("Error").And.Contain("unreadable"));
            Assert.That(Cell(html, error, "services"), Is.EqualTo("0"));
        }

        [Test]
        public async Task WithNoRangeItShowsTheLastSevenDays()
        {
            int today = Add(DateTime.Now.AddMinutes(-1), "ql-today");
            int sixDaysAgo = Add(DateTime.Today.AddDays(-6).AddHours(1), "ql-sixdays");
            int tenDaysAgo = Add(DateTime.Today.AddDays(-10).AddHours(1), "ql-tendays");

            List<int> ids = await RowIdsAcrossPages("/Reports/QuoteLog");

            Assert.That(ids, Does.Contain(today).And.Contain(sixDaysAgo).And.Not.Contain(tenDaysAgo));
        }

        [Test]
        public async Task ItPagesAtFiftyAndShowsTheTotalForTheRange()
        {
            DateTime day = new DateTime(2091, 3, 6);
            for (int i = 0; i < 51; i++) Add(day.AddMinutes(i));

            string first = await Get(Log(day, day, 1));
            string second = await Get(Log(day, day, 2));

            Assert.That(Total(first), Is.EqualTo("51"));
            Assert.That(RowIds(first), Has.Count.EqualTo(50));
            Assert.That(RowIds(second), Has.Count.EqualTo(1));
            Assert.That(RowIds(second).Single(), Is.Not.AnyOf(RowIds(first).Cast<object>().ToArray()));
        }

        [Test]
        public async Task APagePastTheEndShowsTheLastPage()
        {
            DateTime day = new DateTime(2091, 3, 10);
            for (int i = 0; i < 51; i++) Add(day.AddMinutes(i));

            string html = await Get(Log(day, day, 99));

            Assert.That(RowIds(html), Has.Count.EqualTo(1));
            Assert.That(html, Does.Contain("Page 2 of 2"));
            Assert.That(await Get(Log(day, day, 2147483647)), Does.Contain("Page 2 of 2"));
        }

        [Test]
        public async Task AQuoteFromAnotherDayIsNotListed()
        {
            DateTime day = new DateTime(2091, 3, 7);
            int mine = Add(day.AddHours(12));
            Add(day.AddDays(1).AddHours(12));

            string html = await Get(Log(day, day));

            Assert.That(RowIds(html), Is.EqualTo(new[] { mine }));
            Assert.That(Total(html), Is.EqualTo("1"));
        }

        [Test]
        public async Task WithNoUpsRateReadOnlySettingThePageSaysItIsNotConfigured()
        {
            using (var unconfigured = HostWith(null))
            {
                string html = await Get("/Reports/QuoteLog", unconfigured);

                Assert.That(html, Does.Contain("not configured").And.Contain("UpsRateReadOnly"));
                Assert.That(html, Does.Not.Contain("data-request="));
            }
        }

        [Test]
        public async Task NoStreetAddressAndNoRawJsonAreShown()
        {
            DateTime day = new DateTime(2091, 3, 8);
            Add(day.AddHours(12), requestJson: RequestJson(street: "742 Evergreen Terrace"), address: "742 Evergreen Terrace");

            string html = await Get(Log(day, day));

            Assert.That(html, Does.Contain("data-request="));
            Assert.That(html, Does.Not.Contain("Evergreen").And.Not.Contain("RateResponse").And.Not.Contain("RateRequest").And.Not.Contain("AddressLine"));
        }

        [Test]
        public async Task LoadingThePageChangesNothing()
        {
            DateTime day = new DateTime(2091, 3, 9);
            Add(day.AddHours(12));
            int requests = CountOf("Requests_Rate"), requestLog = CountOf("UpsRateRequestLog"), ltl = CountOf("Log_LTL"), calcs = CountOf("QuoteServiceCalculation"), plants = CountOf("RateRequestPlant");

            await Get(Log(day, day));
            await Get("/Reports/QuoteLog");

            Assert.That(CountOf("Requests_Rate"), Is.EqualTo(requests));
            Assert.That(CountOf("UpsRateRequestLog"), Is.EqualTo(requestLog));
            Assert.That(CountOf("Log_LTL"), Is.EqualTo(ltl));
            Assert.That(CountOf("QuoteServiceCalculation"), Is.EqualTo(calcs));
            Assert.That(CountOf("RateRequestPlant"), Is.EqualTo(plants), "the catch-up procedures write; the Quote Log must not call them");
        }

        [Test]
        public void TheGuardRefusesADatabaseNamedUpsRate_ForTheQuoteLogSettingToo()
        {
            Assert.That(() => SandboxGuard.RequireNotUpsRate("Server=AZUREDB01\\AZUREDB01;Initial Catalog=UPSRate;uid=u;pwd=p;"), Throws.InstanceOf<AssertionException>());
            Assert.That(() => SandboxGuard.RequireNotUpsRate("Server=AZUREDB01\\AZUREDB01;Initial Catalog=upsrate;uid=u;pwd=p;"), Throws.InstanceOf<AssertionException>());
            Assert.That(() => SandboxGuard.RequireNotUpsRate("Server=AZUREDB01\\AZUREDB01;Initial Catalog=FreightEstimator;uid=u;pwd=p;"), Throws.Nothing);
        }
    }
}
