using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class Shipment
    {
        public string PlantId { get; set; }
        public string AcctNum { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string Country { get; set; }
        public int number_of_packages { get; set; }
        public float package_weight { get; set; }
        public float last_package_weight { get; set; }
        public int delivery_signature_required { get; set; }
        public bool multiple_location_rate { get; set; }
        public bool include_ground_rate { get; set; }
        public bool include_ltl_rate { get; set; }

        public float billing_weight { get; set; }

        public ShopRateResponse shopRateResponse { get; set; }
    }
}