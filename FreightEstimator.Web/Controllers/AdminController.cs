using AuthenticationServer.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AuthenticationServer.Controllers
{
    /// <summary>The admin area (ADR 0006): open only to the users in Admin:AllowedUsers. Its first page is the Poller Monitor.</summary>
    [AdminOnly]
    public class AdminController : Controller
    {
        public ActionResult PollerMonitor() => View();
    }
}
