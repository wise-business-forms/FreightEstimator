using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace AuthenticationServer.Models
{
    public class Shipment
    {
        public string PlantId { get; set; }
        public string PlantName { get; set; }
        public string AcctNum { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State_selection { get; set; }
        public List<SelectListItem> state {  get; set; }
        public string Zip { get; set; }
        public string Country_selection { get; set; }
        public List<SelectListItem> country { get; set; }
        public int number_of_packages { get; set; }
        public float package_weight { get; set; }
        public float last_package_weight { get; set; }
        public string delivery_signature_required_selection { get; set; }
        public List<SelectListItem> delivery_signature_required { get; set; }
        public string multiple_location_rate_selection { get; set; }
        public List<SelectListItem> multiple_location_rate { get; set; }
        public string include_ground_rate_selection { get; set; }
        public List<SelectListItem> include_ground_rate { get; set; }
        public string include_ltl_rate_selection { get; set; }
        public List<SelectListItem> include_ltl_rate { get; set; }

        // LTL information
        public List<SelectListItem> freight_class {  get; set; }
        public string default_freight_class { get; set; }
        public int freight_class_selected { get; set; }
        public DateTime pick_up_date { get; set; }
        public bool notify_before_delivery {  get; set; }
        public bool liftgate_pickup {  get; set; }
        public bool liftgate_delivery {  get; set; }
        public bool limited_access_pickup { get; set; }
        public bool limited_access_delivery { get; set; }
        public bool residential_pickup { get; set; }
        public bool residential_delivery { get; set; }
        public bool inside_pickup { get; set; }
        public bool inside_delivery { get; set; }
        public bool sort_and_segregate { get; set; }
        public bool stopoff_charge {  get; set; }

        // Calculated values
        public float billing_weight { get; set; }
        public string Corrected_Address { get; set; }
        public string Corrected_City { get; set; }
        public string Corrected_State_selection { get; set; }
        public string Corrected_state { get; set; }
        public string Corrected_Zip { get; set; }
        public string Corrected_Country { get; set; }

        /// <summary>
        /// GRID 1 - All service rates for plant.
        /// </summary>
        public ShopRateResponse[] shopCompareRates { get; set; }
        public MultipleLocations MultipleLocations { get; set; }
        /// <summary>
        /// GRID 2 - Ground Freight
        /// </summary>
        public ShopRateResponse shopGroundFreightResponse { get; set; }
        /// <summary>
        /// GRID 3 - Less Than Truckload (LTL) Rates
        /// </summary>
        public ShopRateResponse shopLessThanTruckloadResponse { get; set; }

        // Logging
        public string requestMessage { get; set; }
        public string responseMessage { get; set; }
        public string results { get; set; }
        public string user_name { get; set; }
    }

    public class SelectedItem
    {
        public string Value { get; set; }
        public string Text { get; set; }
    }

    public class MultipleLocations
    {
        public string Service { get; set; } = string.Empty;
        public string ALP { get; set; }
        public string BUT { get; set; }
        public string FTW { get; set; }
        public string POR { get; set; }
        public string CWT { get; set; }
    }
}