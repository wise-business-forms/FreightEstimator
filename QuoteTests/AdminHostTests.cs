using System.Security.Claims;
using System.Text.Encodings.Web;
using AuthenticationServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// The admin area (ADR 0006, #47) in the running app: the Poller Monitor page is open only to the users named in
    /// Admin:AllowedUsers, and the Admin navbar item shows only to them. A test sign-in (the X-Test-User header) stands in
    /// for IIS Windows authentication, which these tests cannot reach; the IIS settings are documented and checked by hand.
    /// </summary>
    public class AdminHostTests
    {
        private const string Page = "/Admin/PollerMonitor";
        private const string Allowed = "WBF\\pmenefee";

        [TearDown]
        public void TearDown() => Configuration.ConnectionStringSource = null;   // the app sets it at startup; a later test must not see a disposed host's

        private static WebApplicationFactory<Program> App(string environment, params string[] allowedUsers) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment(environment);
                b.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(allowedUsers.Select((user, i) => new KeyValuePair<string, string>("Admin:AllowedUsers:" + i, user))));
                b.ConfigureTestServices(services => services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestSignIn.Scheme;
                        options.DefaultChallengeScheme = TestSignIn.Scheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestSignIn>(TestSignIn.Scheme, _ => { }));
            });

        private static HttpClient ClientFor(WebApplicationFactory<Program> app, string user = null)
        {
            HttpClient client = app.CreateClient();
            if (user != null) client.DefaultRequestHeaders.Add(TestSignIn.Header, user);
            return client;
        }

        [Test]
        public async Task AnAllowedUserOpensThePollerMonitor()
        {
            using var app = App("Production", Allowed);

            HttpResponseMessage response = await ClientFor(app, Allowed).GetAsync(Page);

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Poller Monitor"));
        }

        [Test]
        public async Task AnotherSignedInUserIsForbidden()
        {
            using var app = App("Production", Allowed);

            HttpResponseMessage response = await ClientFor(app, "WBF\\someoneelse").GetAsync(Page);

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task NobodySignedInIsForbidden_AsIisNormallyAsksThemToSignInFirst()
        {
            using var app = App("Production", Allowed);

            HttpResponseMessage response = await ClientFor(app).GetAsync(Page);

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task ProductionDeniesEveryoneWhenNoUsersAreNamed()
        {
            using var app = App("Production");   // Admin:AllowedUsers not set

            HttpResponseMessage response = await ClientFor(app, Allowed).GetAsync(Page);

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task DevelopmentOpensThePageWithNoUserCheck()
        {
            SandboxGuard.Require();   // a Development run must be on the FreightEstimator test database
            using var app = App("Development");

            HttpResponseMessage response = await ClientFor(app).GetAsync(Page);

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        }

        [Test]
        public async Task TheAdminNavbarItemShowsOnlyToAnAllowedUser()
        {
            using var app = App("Production", Allowed);

            string forAllowed = await (await ClientFor(app, Allowed).GetAsync("/Reports")).Content.ReadAsStringAsync();
            string forOther = await (await ClientFor(app, "WBF\\someoneelse").GetAsync("/Reports")).Content.ReadAsStringAsync();
            string forNobody = await (await ClientFor(app).GetAsync("/Reports")).Content.ReadAsStringAsync();

            Assert.That(forAllowed, Does.Contain("href=\"" + Page + "\""));
            Assert.That(forOther, Does.Not.Contain(Page));
            Assert.That(forNobody, Does.Not.Contain(Page));
        }

        [Test]
        public async Task ReportsStayOpenToEveryone()
        {
            using var app = App("Production", Allowed);

            HttpResponseMessage response = await ClientFor(app).GetAsync("/Reports");

            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Poller Shadow Run"));
        }

        /// <summary>Signs a request in as the user named in the X-Test-User header; no header, nobody signed in.</summary>
        private class TestSignIn : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public const string Scheme = "Test";
            public const string Header = "X-Test-User";

            public TestSignIn(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
                : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue(Header, out var user)) return Task.FromResult(AuthenticateResult.NoResult());
                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user.ToString()) }, Scheme);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
            }
        }
    }
}
