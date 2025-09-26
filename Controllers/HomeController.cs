using AuthenticationServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace AuthenticationServer.Controllers
{
    /// <summary>
    /// Home controller that handles the initial application entry point and plant selection.
    /// This controller serves as the landing page for the FreightEstimator application,
    /// allowing users to select a shipping plant/location before proceeding to rate calculations.
    /// </summary>
    /// <remarks>
    /// The HomeController is responsible for:
    /// - Displaying the main landing page with available shipping plants
    /// - Providing plant selection functionality
    /// - Redirecting users to the plant-specific shipping interface
    /// 
    /// This controller works in conjunction with the Plant model to retrieve
    /// active shipping locations from the database.
    /// </remarks>
    public class HomeController : Controller
    {
        /// <summary>
        /// Displays the main home page with available shipping plants for selection.
        /// </summary>
        /// <returns>
        /// A view containing the list of active shipping plants that users can select from.
        /// Each plant represents a different shipping location with its own rate configurations.
        /// </returns>
        /// <remarks>
        /// This action method:
        /// - Sets the page title for the view
        /// - Retrieves all active plants from the database using Plant.Plants()
        /// - Passes the plant list to the view via ViewBag.Plants
        /// - Returns the Index view which displays plant selection buttons
        /// 
        /// The plants are filtered to only show active locations with valid UPS shipping numbers.
        /// </remarks>
        public ActionResult Index()
        {
            ViewBag.Title = "Home Page";
            ViewBag.Plants = Plant.Plants();

            return View();
        }

        /// <summary>
        /// Redirects the user to a specific plant's shipping interface based on the selected plant code.
        /// </summary>
        /// <param name="loc">
        /// The plant location code (e.g., "ALP", "BUT", "FTW", "POR", "AND") that identifies
        /// which shipping plant the user wants to use for rate calculations.
        /// </param>
        /// <returns>
        /// A redirect to the Plant controller's Index action with the selected plant location
        /// as a parameter, allowing the user to proceed with shipment rate calculations.
        /// </returns>
        /// <remarks>
        /// This action method:
        /// - Accepts a plant location code from the user's selection
        /// - Constructs a URL to the Plant controller's Index action
        /// - Passes the plant location as a route parameter
        /// - Redirects the user to the plant-specific shipping interface
        /// 
        /// This method serves as a bridge between the plant selection page and the
        /// actual shipping rate calculation interface.
        /// 
        /// Example usage:
        /// - User clicks "Alpharetta (ALP)" button
        /// - This method receives "ALP" as the loc parameter
        /// - User is redirected to /Plant/Index?loc=ALP
        /// </remarks>
        public ActionResult RedirectToPlant(string loc)
        {
            string targetUrl = Url.Action("Index", "Plant", new { loc = loc });
            return Redirect(targetUrl);
        }
    }
}
