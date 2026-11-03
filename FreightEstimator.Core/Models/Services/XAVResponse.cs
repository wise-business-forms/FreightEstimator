using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.Models.Carrier.UPS
{
    public class XAVResponse
    {
        public Response Response { get; set; }
        public string ValidAddressIndicator { get; set; }
        public AddressClassification AddressClassification { get; set; }
        public Candidate Candidate { get; set; }
    }
}