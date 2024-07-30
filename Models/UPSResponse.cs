using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using AuthenticationServer.Models.Services;

namespace AuthenticationServer.Models
{
    public class AddressClassification
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class AddressKeyFormat
    {
        public string ConsigneeName { get; set; }
        public string BuildingName { get; set; }
        public string[] AddressLine { get; set; }
        public string PoliticalDivision2 { get; set; }
        public string PoliticalDivision1 { get; set; }
        public string PostcodePrimaryLow { get; set; }
        public string PostcodeExtendedLow { get; set; }
        public string[] Region { get; set; }
        public string Urbanization { get; set; }
        public string CountryCode { get; set; }
    }

    public class Candidate
    {
        public AddressClassification AddressClassification { get; set; }
        public AddressKeyFormat AddressKeyFormat { get; set; }
    }

    public class RateResponse
    {
        public Response Response { get; set; }
        public List<RatedShipment> RatedShipment { get; set; }
    }

    public class RatedShipment
    {
        public Service Service { get; set; }
        public TotalCharges TotalCharges { get; set; }
    }

    public class ResponseStatus
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class Response
    {
        public ResponseStatus ResponseStatus { get; set; }
    }

    public class Service
    {
        public string Code { get; set; }
        public string Description { get; set; }
    }

    public class TotalCharges
    {
        public string CurrencyCode { get; set; }
        public string MonetaryValue { get; set; }
    }    

    public class UPSResponse
    {
        public XAVResponse XAVResponse { get; set; }
    }
}