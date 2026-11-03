using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AuthenticationServer.Models
{
    /// <summary>
    /// The Plant page's model: a <see cref="Shipment"/> plus the dropdown lists its form shows. The lists are MVC types, so
    /// they live here, in the web project, and the estimation core (which is also built for .NET 8) holds only the shipment.
    /// </summary>
    public class ShipmentView : Shipment
    {
        public List<SelectListItem> state { get; set; }
        public List<SelectListItem> country { get; set; }
        public List<SelectListItem> delivery_signature_required { get; set; }
        public List<SelectListItem> multiple_location_rate { get; set; }
        public List<SelectListItem> include_ground_rate { get; set; }
        public List<SelectListItem> include_ltl_rate { get; set; }
        public List<SelectListItem> freight_class { get; set; }
    }

    public static class ShipmentViewLists
    {
        public static readonly List<SelectListItem> DeliverySignatureRequired = new List<SelectListItem>()
        {
            new SelectListItem() { Text = "None", Value = "None" },
            new SelectListItem() { Text = "Delivery Confirmation", Value = "Delivery Confirmation" },
            new SelectListItem() { Text = "Del Conf / Signature Req'd", Value = "Delivery Confirmation / Signature Required" },
            new SelectListItem() { Text = "Del Conf / Adult Sig Req'd", Value = "Delivery Confirmation / Adult Signature Required" }
        };
    }
}
