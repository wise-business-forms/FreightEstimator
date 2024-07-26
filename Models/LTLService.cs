using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class LTLService
    {
        public string PlantCode {  get; set; }
        public string Carrier { get; set; }
        public double TotalCharges { get; set; }
        public double Cost { get; set; }
        public int TransitDays { get; set; }
        public string Direct { get; set; }
    }
}