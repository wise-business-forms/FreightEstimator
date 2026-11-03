using System;
using System.Collections.Generic;
using System.Configuration;
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
    /// available rather than returning a number the business already considers wrong.
    ///
    /// A service is available when the API can quote it (it is in <see cref="Implemented"/>) AND it has been switched
    /// on. The switch is the appSetting QuoteApi.AvailableServices, a comma-separated list of service names, empty
    /// when absent. It exists so that making a service available is a deliberate act, taken after the ADR 0003 gate
    /// (a reviewed sample of shadow-mode divergences) has passed for that path, not a side effect of deploying code.
    /// Each cutover ticket (#19 GRID2, #20 GRID1 single plant, #21 LTL) adds its services to <see cref="Implemented"/>.
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

        /// <summary>The services the API has a Calculated Rate path for: UPS Ground Freight (GRID2), the UPS parcel services (GRID1 single plant) and LTL.</summary>
        private static readonly string[] Implemented = All.Select(s => s.Name).ToArray();

        private const string SwitchSetting = "QuoteApi.AvailableServices";

        // A test replaces the switch without touching configuration. See Use.
        private static IReadOnlyCollection<string> _switchedOnForTests;

        /// <summary>Whether the service can be quoted through the API yet.</summary>
        public static bool IsAvailable(CatalogService service) =>
            Contains(Implemented, service.Name) && Contains(SwitchedOn(), service.Name);

        private static IEnumerable<string> SwitchedOn() =>
            _switchedOnForTests ?? (ConfigurationManager.AppSettings[SwitchSetting] ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

        private static bool Contains(IEnumerable<string> names, string name) =>
            names.Contains(name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Switches exactly these services on until the returned scope is disposed. For tests.</summary>
        internal static IDisposable Use(params string[] switchedOn)
        {
            IReadOnlyCollection<string> previous = _switchedOnForTests;
            _switchedOnForTests = switchedOn;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly IReadOnlyCollection<string> _previous;
            public Restore(IReadOnlyCollection<string> previous) { _previous = previous; }
            public void Dispose() { _switchedOnForTests = _previous; }
        }

        private static CatalogService Parcel(UPSService.ServiceCode code) =>
            new CatalogService { Name = code.ToString(), Carrier = Ups };
    }
}
