using AuthenticationServer.CostPlus;
using AuthenticationServer.Models.Services;
using FreightEstimator.Tests.Fakes;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Runs one CostPlus poll cycle the way the poller tests need it: the carrier fake installed, known Plant Charges for the
    /// plant, and the UPS parcel services switched on (a Service that is not available is never quoted, CONTEXT.md: Service
    /// Availability). Everything is put back when the cycle ends.
    /// </summary>
    internal static class CostPlusCycle
    {
        public static readonly string[] ParcelServices =
            { "UPSGround", "UPS3DaySelect", "UPS2ndDayAir", "SecondDayAirAM", "NextDayAirSaver", "UPSNextDayAir", "NextDayAirEarlyAM" };

        /// <param name="plant">The plant whose Plant Charges are set up.</param>
        /// <param name="schedule">Its Plant Charges; the known schedule by default. An empty array leaves the plant with none.</param>
        /// <param name="available">The services switched on; every UPS parcel service by default.</param>
        public static int Run(FakeCarrierHttp fake, string plant = "ALP", SandboxPlantCharges.Row[] schedule = null, string[] available = null)
        {
            using (new SandboxPlantCharges(plant, schedule ?? SandboxPlantCharges.KnownSchedule()))
            using (ServiceCatalog.Use(available ?? ParcelServices))
            using (fake.Install())
            {
                return new CostPlusPollCycle().RunOnce();
            }
        }
    }
}
