using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// Runs the CostPlus poller inside the web app (ADR 0005): poll cycle after poll cycle, with a short pause when there
    /// is nothing to answer. Off unless CostPlus:Poller:Enabled is true, so a developer machine or a test host never
    /// answers requests by accident. IIS runs the app in-process, so the app pool must stay up (AlwaysRunning, Idle
    /// Timeout 0, Preload Enabled; docs/aspnetcore-iis.md).
    /// </summary>
    internal class CostPlusPollerService : BackgroundService
    {
        /// <summary>The console app sleeps 250 ms when it finds nothing; the poller keeps that pace.</summary>
        private static readonly TimeSpan IdlePause = TimeSpan.FromMilliseconds(250);

        /// <summary>After a failed cycle (database down, for instance) the poller waits longer before trying again.</summary>
        private static readonly TimeSpan FailurePause = TimeSpan.FromSeconds(5);

        private readonly bool _enabled;
        private readonly ILogger<CostPlusPollerService> _logger;

        public CostPlusPollerService(IConfiguration configuration, ILogger<CostPlusPollerService> logger)
        {
            _enabled = configuration.GetValue<bool>("CostPlus:Poller:Enabled");
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("The CostPlus poller is switched off (CostPlus:Poller:Enabled).");
                return;
            }

            _logger.LogInformation("The CostPlus poller has started.");
            // Task.Yield: BackgroundService runs this synchronously up to the first await, which would hold up host startup.
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan pause = TimeSpan.Zero;
                try
                {
                    int answered = new CostPlusPollCycle().RunOnce();
                    if (answered == 0) pause = IdlePause;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "A CostPlus poll cycle failed.");
                    pause = FailurePause;
                }

                if (pause > TimeSpan.Zero)
                {
                    try { await Task.Delay(pause, stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }

            _logger.LogInformation("The CostPlus poller has stopped.");
        }
    }
}
