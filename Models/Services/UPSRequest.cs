using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Web;

namespace AuthenticationServer.Models.Services
{
    public class UPSRequest
    {
        public string XAVRequest { get; set; }
        public AddressKeyFormat AddressKeyFormat { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}