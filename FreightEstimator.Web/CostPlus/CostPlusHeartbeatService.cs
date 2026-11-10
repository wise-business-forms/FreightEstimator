using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// Writes this CostPlus Poller instance's heartbeat every 10 s (#50), so the Poller Monitor can show every instance
    /// whichever process serves the page. A service of its own, apart from the poll loop, and best-effort: a heartbeat that
    /// cannot be written is logged and dropped, and never delays or changes how a request is answered. Off unless the poller is on.
    /// </summary>
    internal class CostPlusHeartbeatService : BackgroundService
    {
        private static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

        private readonly bool _enabled;
        private readonly string _mode;
        private readonly string _instanceId = Environment.MachineName + ":" + Environment.ProcessId;
        private readonly ILogger<CostPlusHeartbeatService> _logger;
        private readonly CostPlusPollerHealth _health;
        private readonly IPollerHeartbeatStore _store;
        private bool _failing;

        public CostPlusHeartbeatService(IConfiguration configuration, ILogger<CostPlusHeartbeatService> logger, CostPlusPollerHealth health, IPollerHeartbeatStore store)
        {
            _enabled = configuration.GetValue<bool>("CostPlus:Poller:Enabled");
            _mode = CostPlusPollerMode.IsShadow(configuration) ? "Shadow" : "Live";
            _logger = logger;
            _health = health;
            _store = store;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled) return;
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                WriteOnce();

                try { await Task.Delay(Every, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>Writes one heartbeat. Never throws. A failure is logged when it starts and again when it ends, not every 10 s.</summary>
        internal void WriteOnce()
        {
            try
            {
                _store.Write(_health.ToBeat(_instanceId, Environment.MachineName, _mode));
                if (_failing) _logger.LogInformation("The CostPlus heartbeat can be written again.");
                _failing = false;
            }
            catch (Exception ex)
            {
                if (!_failing) _logger.LogError(ex, "The CostPlus heartbeat could not be written; the poller carries on without it.");
                _failing = true;
            }
        }
    }
}
