using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models.Service
{
    public class XAVResponse
    {
        public Response Response { get; set; }
        public string ValidAddressIndicator { get; set; }
        public AddressClassification AddressClassification { get; set; }
        public Candidate Candidate { get; set; }
    }
}