using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class TICharge
    {
        public TICharge() { }
        public string Type { get; set; }
        public string Description { get; set; }
        public string EditCode { get; set; }
        public double Amount { get; set; }
        public double Rate { get; set; }
        public double Quantity { get; set; }
    }
}