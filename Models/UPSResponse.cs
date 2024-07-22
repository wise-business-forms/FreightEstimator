using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class ResponseStatus
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class Response
    {
        public ResponseStatus ResponseStatus { get; set; }
    }

    public class AddressClassification
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class AddressKeyFormat
    {
        public string AddressLine { get; set; }
        public string PoliticalDivision2 { get; set; }
        public string PoliticalDivision1 { get; set; }
        public string PostcodePrimaryLow { get; set; }
        public string PostcodeExtendedLow { get; set; }
        public string Region { get; set; }
        public string CountryCode { get; set; }
    }

    public class Candidate
    {
        public AddressClassification AddressClassification { get; set; }
        public AddressKeyFormat AddressKeyFormat { get; set; }
    }

    public class XAVResponse
    {
        public Response Response { get; set; }
        public string ValidAddressIndicator { get; set; }
        public AddressClassification AddressClassification { get; set; }
        public Candidate Candidate { get; set; }
    }

    public class UPSResponse
    {
        public XAVResponse XAVResponse { get; set; }
    }
}