using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuthenticationServer.Models.Services
{
    public enum Vendor { UPS, FEDX }

    public class RateResponse
    {
        public string URI { get; }
        public string Source { get; set; }
        public string PlantCode { get; set; }
        public string DestinationAddress1 { get; set; }
        public string DestinationAddress2 { get; set; }
        public string DestinationAddress3 { get; set; }
        public string DestinationCity { get; set; }
        public string DestinationState { get; set; }
        public string DestinationPostalCode { get; set; }
        public int Weight { get; set; }
        public bool LFTG_PU { get; set; }
        public bool LFTG_D { get; set; }
        public bool LAP { get; set; }
        public bool LAPU { get; set; }
        public bool RESD { get; set; }
        public bool IND { get; set; }
        public bool IPU { get; set; }
        public bool SS { get; set; }
        public bool STPO { get; set; }
        public string[] Service { get; set; }
        public string[] Carrier { get; set; }
        public string[] Rate { get; set; }
    }
}
