using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace AuthenticationServer.Models
{
    public class Geography

    {
        public static SelectList Countries()
        {
            var countries = new List<SelectListItem>
            {
                new SelectListItem { Value = "US", Text="United States"},
                new SelectListItem { Value = "PR", Text="Puerto Rico"}
            };
            return new SelectList(countries, "Value", "Text");
        }
        public static SelectList States()
        {
            var states = new List<SelectListItem>
            {
                new SelectListItem { Value = "AL", Text="ALABAMA"},
                new SelectListItem { Value = "AK", Text="ALASKA "},
                new SelectListItem { Value = "AS", Text="AMERICAN SAMOA "},
                new SelectListItem { Value = "AZ", Text="ARIZONA"},
                new SelectListItem { Value = "AR", Text="ARKANSAS"},
                new SelectListItem { Value = "CA", Text="CALIFORNIA"},
                new SelectListItem { Value = "CO", Text="COLORADO"},
                new SelectListItem { Value = "CT", Text="CONNECTICUT"},
                new SelectListItem { Value = "DE", Text="DELAWARE"},
                new SelectListItem { Value = "DC", Text="DISTRICT OF COLUMBIA"},
                new SelectListItem { Value = "FL", Text="FLORIDA"},
                new SelectListItem { Value = "GA", Text="GEORGIA"},
                new SelectListItem { Value = "GU", Text="GUAM"},
                new SelectListItem { Value = "HI", Text="HAWAII"},
                new SelectListItem { Value = "ID", Text="IDAHO"},
                new SelectListItem { Value = "IL", Text="ILLINOIS"},
                new SelectListItem { Value = "IN", Text="INDIANA"},
                new SelectListItem { Value = "IA", Text="IOWA"},
                new SelectListItem { Value = "KS", Text="KANSAS"},
                new SelectListItem { Value = "KY", Text="KENTUCKY"},
                new SelectListItem { Value = "LA", Text="LOUISIANA"},
                new SelectListItem { Value = "ME", Text="MAINE"},
                new SelectListItem { Value = "MD", Text="MARYLAND"},
                new SelectListItem { Value = "MA", Text="MASSACHUSETTS"},
                new SelectListItem { Value = "MI", Text="MICHIGAN"},
                new SelectListItem { Value = "MN", Text="MINNESOTA"},
                new SelectListItem { Value = "MS", Text="MISSISSIPPI"},
                new SelectListItem { Value = "MO", Text="MISSOURI"},
                new SelectListItem { Value = "MT", Text="MONTANA"},
                new SelectListItem { Value = "NE", Text="NEBRASKA"},
                new SelectListItem { Value = "NV", Text="NEVADA"},
                new SelectListItem { Value = "NH", Text="NEW HAMPSHIRE"},
                new SelectListItem { Value = "NJ", Text="NEW JERSEY"},
                new SelectListItem { Value = "NM", Text="NEW MEXICO"},
                new SelectListItem { Value = "NY", Text="NEW YORK"},
                new SelectListItem { Value = "NC", Text="NORTH CAROLINA"},
                new SelectListItem { Value = "ND", Text="NORTH DAKOTA"},
                new SelectListItem { Value = "MP", Text="NORTHERN MARIANA IS"},
                new SelectListItem { Value = "OH", Text="OHIO"},
                new SelectListItem { Value = "OK", Text="OKLAHOMA"},
                new SelectListItem { Value = "OR", Text="OREGON"},
                new SelectListItem { Value = "PA", Text="PENNSYLVANIA"},
                new SelectListItem { Value = "PR", Text="PUERTO RICO"},
                new SelectListItem { Value = "RI", Text="RHODE ISLAND"},
                new SelectListItem { Value = "SC", Text="SOUTH CAROLINA"},
                new SelectListItem { Value = "SD", Text="SOUTH DAKOTA"},
                new SelectListItem { Value = "TN", Text="TENNESSEE"},
                new SelectListItem { Value = "TX", Text="TEXAS"},
                new SelectListItem { Value = "UT", Text="UTAH"},
                new SelectListItem { Value = "VT", Text="VERMONT"},
                new SelectListItem { Value = "VA", Text="VIRGINIA"},
                new SelectListItem { Value = "VI", Text="VIRGIN ISLANDS"},
                new SelectListItem { Value = "WA", Text="WASHINGTON"},
                new SelectListItem { Value = "WV", Text="WEST VIRGINIA"},
                new SelectListItem { Value = "WI", Text="WISCONSIN"},
                new SelectListItem { Value = "WY", Text="WYOMING"}
            };
            return new SelectList(states, "Value", "Text");
        }
    }
}