using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class UPSService
    {
        public string ShipFrom { get; set; }
        public string ServiceName { get; set; }
        public float Rate { get; set; }
        public bool CWT { get; set; }
    }
}