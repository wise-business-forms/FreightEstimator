using AuthenticationServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace AuthenticationServer.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Title = "Home Page";
            ViewBag.Plants = Plant.Plants();

            return View();
        }

        public ActionResult RedirectToPlant(string loc)
        {
            string targetUrl = Url.Action("Index", "Plant", new { loc = loc });
            return Redirect(targetUrl);
        }
    }
}
