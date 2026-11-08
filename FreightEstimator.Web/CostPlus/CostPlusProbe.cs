using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using AuthenticationServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// The probe request (#35, ADR 0005): a test request the poller puts in the CostPlus request table in quiet hours, so a
    /// stall is noticed when nobody is asking. It is marked by a reserved LoginId prefix (the request table has no test column and
    /// is CostPlus's to change). The poll cycle answers a probe without a Plant, a Quote or a UPS call, so a probe never reaches UPS
    /// or reporting; if the poller is stalled the probe just sits unclaimed, and the health check goes unhealthy.
    /// </summary>
    internal static class CostPlusProbe
    {
        public const string LoginIdPrefix = "CostPlusProbe-";

        public static bool IsProbe(string loginId) => loginId != null && loginId.StartsWith(LoginIdPrefix, StringComparison.Ordinal);

        /// <summary>Whether a time of day falls in the quiet hours [start, end); the window may run past midnight.</summary>
        public static bool IsQuietHour(TimeSpan timeOfDay, TimeSpan start, TimeSpan end) =>
            start <= end ? timeOfDay >= start && timeOfDay < end : timeOfDay >= start || timeOfDay < end;

        /// <summary>Puts one probe request in the table (database clock) and removes probes over an hour old, answered or not, so a long stall cannot pile them up.</summary>
        public static void Insert()
        {
            using (var conn = new SqlConnection(Configuration.UpsRateSqlConnection))
            {
                conn.Open();
                using (var cmd = new SqlCommand(
                    "DELETE f FROM dbo.FreightRequests f WHERE f.LoginId LIKE @prefix + '%' AND ISNULL(f.DateRated, f.DateRequested) < DATEADD(HOUR, -1, GETDATE()); " +
                    "INSERT dbo.FreightRequests (LoginId, QtyNumber, FromState, RequestUPS, RequestLTL, NumPackages, PkgWeight, LastPkgWeight, DateRequested) " +
                    "VALUES (@prefix + CONVERT(VARCHAR(36), NEWID()), 1, 'GA', 1, 0, 1, 1, 1, GETDATE())", conn))
                {
                    cmd.Parameters.Add("@prefix", SqlDbType.VarChar, 50).Value = LoginIdPrefix;
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

    /// <summary>
    /// Inserts a probe every CostPlus:Probe:IntervalMinutes (5) during quiet hours (CostPlus:Probe:QuietStart/QuietEnd, local
    /// time, 22:00 to 05:00). Off unless the poller is on, and off if CostPlus:Probe:Enabled is false.
    /// </summary>
    internal class CostPlusProbeService : BackgroundService
    {
        private readonly bool _enabled;
        private readonly TimeSpan _interval, _quietStart, _quietEnd;
        private readonly ILogger<CostPlusProbeService> _logger;

        public CostPlusProbeService(IConfiguration configuration, ILogger<CostPlusProbeService> logger)
        {
            _enabled = configuration.GetValue<bool>("CostPlus:Poller:Enabled") && configuration.GetValue("CostPlus:Probe:Enabled", true)
                // In shadow mode the console app would answer a probe, and rate it through UPS.
                && !CostPlusPollerMode.IsShadow(configuration);
            _interval = TimeSpan.FromMinutes(configuration.GetValue("CostPlus:Probe:IntervalMinutes", 5));
            _quietStart = TimeSpan.Parse(configuration["CostPlus:Probe:QuietStart"] ?? "22:00");
            _quietEnd = TimeSpan.Parse(configuration["CostPlus:Probe:QuietEnd"] ?? "05:00");
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled) return;
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (CostPlusProbe.IsQuietHour(DateTime.Now.TimeOfDay, _quietStart, _quietEnd)) CostPlusProbe.Insert();
                }
                catch (Exception ex) { _logger.LogError(ex, "The CostPlus probe request could not be inserted."); }

                try { await Task.Delay(_interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
