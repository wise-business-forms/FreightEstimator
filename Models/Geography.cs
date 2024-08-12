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
                new SelectListItem { Value = "AL", Text="AL"},
                new SelectListItem { Value = "AK", Text="AK"},
                new SelectListItem { Value = "AS", Text="AS"},
                new SelectListItem { Value = "AZ", Text="AZ"},
                new SelectListItem { Value = "AR", Text="AR"},
                new SelectListItem { Value = "CA", Text="CA"},
                new SelectListItem { Value = "CO", Text="CO"},
                new SelectListItem { Value = "CT", Text="CT"},
                new SelectListItem { Value = "DE", Text="DE"},
                new SelectListItem { Value = "DC", Text="DC"},
                new SelectListItem { Value = "FL", Text="FL"},
                new SelectListItem { Value = "GA", Text="GA"},
                new SelectListItem { Value = "GU", Text="GU"},
                new SelectListItem { Value = "HI", Text="HI"},
                new SelectListItem { Value = "ID", Text="ID"},
                new SelectListItem { Value = "IL", Text="IL"},
                new SelectListItem { Value = "IN", Text="IN"},
                new SelectListItem { Value = "IA", Text="IA"},
                new SelectListItem { Value = "KS", Text="KS"},
                new SelectListItem { Value = "KY", Text="KY"},
                new SelectListItem { Value = "LA", Text="LA"},
                new SelectListItem { Value = "ME", Text="ME"},
                new SelectListItem { Value = "MD", Text="MD"},
                new SelectListItem { Value = "MA", Text="MA"},
                new SelectListItem { Value = "MI", Text="MI"},
                new SelectListItem { Value = "MN", Text="MN"},
                new SelectListItem { Value = "MS", Text="MS"},
                new SelectListItem { Value = "MO", Text="MO"},
                new SelectListItem { Value = "MT", Text="MT"},
                new SelectListItem { Value = "NE", Text="NE"},
                new SelectListItem { Value = "NV", Text="NV"},
                new SelectListItem { Value = "NH", Text="NH"},
                new SelectListItem { Value = "NJ", Text="NJ"},
                new SelectListItem { Value = "NM", Text="NM"},
                new SelectListItem { Value = "NY", Text="NY"},
                new SelectListItem { Value = "NC", Text="NC"},
                new SelectListItem { Value = "ND", Text="ND"},
                new SelectListItem { Value = "MP", Text="MP"},
                new SelectListItem { Value = "OH", Text="OH"},
                new SelectListItem { Value = "OK", Text="OK"},
                new SelectListItem { Value = "OR", Text="OR"},
                new SelectListItem { Value = "PA", Text="PA"},
                new SelectListItem { Value = "PR", Text="PR"},
                new SelectListItem { Value = "RI", Text="RI"},
                new SelectListItem { Value = "SC", Text="SC"},
                new SelectListItem { Value = "SD", Text="SD"},
                new SelectListItem { Value = "TN", Text="TN"},
                new SelectListItem { Value = "TX", Text="TX"},
                new SelectListItem { Value = "UT", Text="UT"},
                new SelectListItem { Value = "VT", Text="VT"},
                new SelectListItem { Value = "VA", Text="VA"},
                new SelectListItem { Value = "VI", Text="VI"},
                new SelectListItem { Value = "WA", Text="WA"},
                new SelectListItem { Value = "WV", Text="WV"},
                new SelectListItem { Value = "WI", Text="WI"},
                new SelectListItem { Value = "WY", Text="WY"}
            };
            return new SelectList(states, "Value", "Text");
        }
    }
}