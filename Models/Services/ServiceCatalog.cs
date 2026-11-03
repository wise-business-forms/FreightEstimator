using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.Models.Services
{
    /// <summary>One service a Quote can be asked for, and the carrier that would quote it.</summary>
    public class CatalogService
    {
        public string Name { get; set; }
        public string Carrier { get; set; }
    }

    /// <summary>
    /// The fixed catalog of services the Quote API knows about, and whether each can be quoted yet
    /// (CONTEXT.md: Service Availability). A service is available only once its Calculated Rate path has been
    /// cut over to the unified module (docs/adr/0003, docs/adr/0004); until then the API reports it as not
    /// available rather than returning a number the business already considers wrong. No path has been cut
    /// over yet, so nothing is available; each cutover ticket (#19 GRID2, #20 GRID1 single plant, #21 LTL)
    /// switches its services on here.
    /// </summary>
    public static class ServiceCatalog
    {
        public const string Ups = "UPS";
        public const string LogisticsPartner = "LogisticsPartner";

        /// <summary>Every service, in the order the API lists them. The names are the ones the Plant page uses.</summary>
        public static readonly IReadOnlyList<CatalogService> All = new[]
        {
            Parcel(UPSService.ServiceCode.UPSGround),
            Parcel(UPSService.ServiceCode.UPS3DaySelect),
            Parcel(UPSService.ServiceCode.UPS2ndDayAir),
            Parcel(UPSService.ServiceCode.SecondDayAirAM),
            Parcel(UPSService.ServiceCode.NextDayAirSaver),
            Parcel(UPSService.ServiceCode.UPSNextDayAir),
            Parcel(UPSService.ServiceCode.NextDayAirEarlyAM),
            new CatalogService { Name = "UPSGroundFreight", Carrier = Ups },
            new CatalogService { Name = "LTL", Carrier = LogisticsPartner }
        };

        /// <summary>The catalog entry for a service name, ignoring case, or null if there is no such service.</summary>
        public static CatalogService Find(string name) =>
            All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether the service can be quoted through the API yet.</summary>
        public static bool IsAvailable(CatalogService service) => false;

        private static CatalogService Parcel(UPSService.ServiceCode code) =>
            new CatalogService { Name = code.ToString(), Carrier = Ups };
    }
}
