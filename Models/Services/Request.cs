using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuthenticationServer.Models.Services
{
    public enum Vendor { UPS, FEDX }

    internal interface Request
    {
        string URI { get; }
        Vendor Carrier { get; }
    }
}
