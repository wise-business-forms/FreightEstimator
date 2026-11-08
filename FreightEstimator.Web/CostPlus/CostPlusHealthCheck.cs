using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// Reads both health measures and judges them (#35). Behind the health endpoint and the stall monitor, so the two always agree.
    /// If the request table cannot be read the poller cannot be working either, so that is unhealthy too.
    /// </summary>
    internal class CostPlusHealthCheck
    {
        private readonly CostPlusPollerHealth _health;
        private readonly Func<TimeSpan?> _oldestUnclaimedAge;

        public CostPlusHealthCheck(CostPlusPollerHealth health, IConfiguration configuration)
            : this(health,
                  // In shadow mode the console app owns the queue, so how long a request has waited says nothing about the poller.
                  CostPlusPollerMode.IsShadow(configuration) ? (Func<TimeSpan?>)(() => null) : CostPlusQueue.OldestUnclaimedAge,
                  configuration.GetValue<bool>("CostPlus:Poller:Enabled")) { }

        public CostPlusHealthCheck(CostPlusPollerHealth health, Func<TimeSpan?> oldestUnclaimedAge, bool pollerEnabled)
        {
            _health = health;
            _oldestUnclaimedAge = oldestUnclaimedAge;
            PollerEnabled = pollerEnabled;
        }

        /// <summary>When the poller is off (the console app answers), there is nothing to be healthy or not.</summary>
        public bool PollerEnabled { get; }

        public CostPlusPollerHealth.Report Check()
        {
            TimeSpan? oldest = null;
            string readProblem = null;
            try { oldest = _oldestUnclaimedAge(); }
            catch (Exception ex) { readProblem = "The request table could not be read: " + ex.Message; }

            CostPlusPollerHealth.Report report = _health.Evaluate(oldest);
            if (readProblem != null)
            {
                report.Healthy = false;
                report.Problems.Add(readProblem);
            }
            return report;
        }

        /// <summary>GET /health/costplus-poller: 200 when healthy (or switched off), 503 when not.</summary>
        public static IResult Handle(CostPlusHealthCheck check)
        {
            if (!check.PollerEnabled) return Results.Json(new { status = "disabled" });
            CostPlusPollerHealth.Report report = check.Check();
            return Results.Json(new
            {
                status = report.Healthy ? "healthy" : "unhealthy",
                oldestUnclaimedRequestAgeSeconds = report.OldestUnclaimedRequestAgeSeconds,
                lastSuccessfulPollUtc = report.LastSuccessfulPollUtc,
                secondsSinceLastSuccessfulPoll = report.SecondsSinceLastSuccessfulPoll,
                problems = report.Problems
            }, statusCode: report.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }
    }
}
