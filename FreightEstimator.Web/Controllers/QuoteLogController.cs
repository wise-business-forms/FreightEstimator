using System;
using System.Data.SqlClient;
using AuthenticationServer.Models;
using AuthenticationServer.ProductionQuotes;
using Microsoft.AspNetCore.Mvc;

namespace AuthenticationServer.Controllers
{
    /// <summary>
    /// The Quote Log (#53, ADR 0007): what production has answered, read from UPSRate through the read-only UpsRateReadOnly
    /// connection. Open like the other Reports. Its own controller, routed under Reports, so it shares no code path with Past Quotes.
    /// </summary>
    [Route("Reports/QuoteLog")]
    public class QuoteLogController : Controller
    {
        // GET: Reports/QuoteLog
        [HttpGet("")]
        public ActionResult Index(DateTime? from = null, DateTime? to = null, int page = 1)
        {
            string connection = Configuration.UpsRateReadOnlySqlConnection;
            if (string.IsNullOrWhiteSpace(connection))
                return View("NotConfigured");

            DateTime toDate = (to ?? DateTime.Today).Date;
            DateTime fromDate = (from ?? toDate.AddDays(-(QuoteLogReport.DefaultDays - 1))).Date;
            if (fromDate > toDate) { DateTime swap = fromDate; fromDate = toDate; toDate = swap; }

            using (var conn = new SqlConnection(connection))
            {
                conn.Open();
                ViewBag.From = fromDate;
                ViewBag.To = toDate;
                return View(QuoteLogReport.Load(conn, fromDate, toDate, page));
            }
        }
    }
}
