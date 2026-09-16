using System;
using System.Collections.Generic;

namespace AuthenticationServer.Models
{
    public class ParsedRequestInfo
    {
        public string ShipFromName { get; set; }
        public string ShipFromPlantId { get; set; }
        public string ShipFromCity { get; set; }
        public string ShipFromState { get; set; }
        public string ShipToCity { get; set; }
        public string ShipToState { get; set; }
        public string ShipToZip { get; set; }
        public string ShipToCountry { get; set; }
        public string NumOfPieces { get; set; }
        public List<string> PackageWeights { get; set; } = new List<string>();
        public string ParseError { get; set; }
    }

    public class ParsedRateService
    {
        public string ServiceCode { get; set; }
        public string ServiceName { get; set; }
        public string PublishedRate { get; set; }
        public string NegotiatedRate { get; set; }
        public string TransitDays { get; set; }
    }

    public class RateRequestRecord
    {
        public int RequestId { get; set; }
        public string UserName { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string Country { get; set; }
        public DateTime? RequestDate { get; set; }
        public ParsedRequestInfo ParsedRequest { get; set; } = new ParsedRequestInfo();
        public List<ParsedRateService> ParsedServices { get; set; } = new List<ParsedRateService>();
        public string ParseError { get; set; }
    }
}
