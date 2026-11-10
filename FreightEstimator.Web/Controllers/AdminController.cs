using System.Collections.Generic;
using System.Linq;
using AuthenticationServer.Admin;
using AuthenticationServer.CostPlus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AuthenticationServer.Controllers
{
    /// <summary>The admin area (ADR 0006): open only to the users in Admin:AllowedUsers. Its first page is the Poller Monitor.</summary>
    [AdminOnly]
    public class AdminController : Controller
    {
        public ActionResult PollerMonitor() => View();

        /// <summary>
        /// The Poller Monitor's status as JSON, re-fetched by the page about every 2 s (#49, #50): which CostPlus Poller instances are
        /// running and whether the poller is healthy, the stall monitor and probe, and the waiting queue. Read-only.
        /// </summary>
        public ActionResult PollerMonitorStatus()
        {
            // Resolved here, not injected: the source is internal, and MVC builds a controller only through a public constructor.
            MonitorReading reading = HttpContext.RequestServices.GetRequiredService<IPollerMonitorSource>().Read();

            var errors = new List<string>();
            if (reading.BeatsError != null) errors.Add(reading.BeatsError);
            if (reading.QueueError != null) errors.Add(reading.QueueError);

            // If the heartbeats could not be read, say nothing about whether a poller is running rather than claim none is.
            CostPlusMonitorStatus status = reading.Beats == null ? null : CostPlusMonitorStatus.Judge(reading.Beats, reading.Queue?.OldestAge);
            QueueSnapshot queue = reading.Queue;

            Response.Headers["Cache-Control"] = "no-store";
            return Json(new
            {
                anyRunning = status?.AnyRunning,
                healthy = status?.Healthy,
                problems = status?.Problems ?? new List<string>(),
                instances = status?.Instances ?? new List<CostPlusMonitorStatus.InstanceStatus>(),
                stallMonitor = new { lastAlertSentAgoSeconds = status?.LastAlertSentAgo?.TotalSeconds },
                probe = new
                {
                    lastInsertedAgoSeconds = status?.LastProbeInsertedAgo?.TotalSeconds,
                    latest = queue?.LatestProbe == null ? null : new { sentAgeSeconds = queue.LatestProbe.SentAgeSeconds, answered = queue.LatestProbe.Answered }
                },
                queue = queue == null ? null : new { waiting = queue.Waiting, oldest = queue.Oldest.Select(Shape).ToList(), inFlight = queue.InFlight.Select(Shape).ToList() },
                errors
            });
        }

        /// <summary>What is shown of a request: who asked, where from and to, which carriers, how long ago. Never a street address.</summary>
        private static object Shape(QueueRequest q) => new
        {
            loginId = q.LoginId, qtyNumber = q.QtyNumber, fromState = q.FromState, toZip = q.ToZip,
            ups = q.Ups, ltl = q.Ltl, isProbe = q.IsProbe, claimedBy = q.ClaimedBy, ageSeconds = q.AgeSeconds
        };
    }
}
