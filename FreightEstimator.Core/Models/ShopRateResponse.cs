using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.Models
{
    public class ShopRateResponse
    {
        public int ResponseStatusCode { get; set; }
        public String[] Alert { get; set; }
        public UPSService[] UPSServices { get; set; }
    }
}