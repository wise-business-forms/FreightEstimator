using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuthenticationServer.CostPlus
{
    /// <summary>Sends a stall alert to a person (#35).</summary>
    internal interface IStallAlertSender
    {
        void Send(string subject, string body);
    }

    /// <summary>Email through the SMTP relay in CostPlus:Alerts:SmtpHost, to CostPlus:Alerts:To (pmenefee@wbf.com by default, ADR 0005).</summary>
    internal class SmtpStallAlertSender : IStallAlertSender
    {
        private readonly string _host, _from, _to;

        public SmtpStallAlertSender(IConfiguration configuration)
        {
            _host = configuration["CostPlus:Alerts:SmtpHost"];
            _to = configuration["CostPlus:Alerts:To"] ?? "pmenefee@wbf.com";
            _from = configuration["CostPlus:Alerts:From"] ?? "freightestimator@wbf.com";
        }

        public void Send(string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(_host))
                throw new InvalidOperationException("CostPlus:Alerts:SmtpHost is not configured, so the alert \"" + subject + "\" could not be emailed.");
            using (var smtp = new SmtpClient(_host))
            using (var mail = new MailMessage(_from, _to, subject, body))
                smtp.Send(mail);
        }
    }

    /// <summary>
    /// Decides when to alert: once when the poller turns unhealthy, again as a reminder while it stays so, and once when it
    /// recovers. Not every check, or a long stall would bury the inbox.
    /// </summary>
    internal class CostPlusStallAlerts
    {
        public static readonly TimeSpan ReminderEvery = TimeSpan.FromMinutes(30);

        private readonly IStallAlertSender _sender;
        private readonly Func<DateTime> _utcNow;
        private bool _unhealthy;
        private DateTime _lastAlertUtc;

        public CostPlusStallAlerts(IStallAlertSender sender, Func<DateTime> utcNow = null)
        {
            _sender = sender;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public void Observe(CostPlusPollerHealth.Report report)
        {
            DateTime now = _utcNow();
            if (!report.Healthy)
            {
                if (_unhealthy && now - _lastAlertUtc < ReminderEvery) return;
                // Marked as alerted only once the send worked, so a failed send is tried again at the next check.
                _sender.Send(_unhealthy ? "CostPlus poller still stalled" : "CostPlus poller stalled", string.Join(Environment.NewLine, report.Problems));
                _unhealthy = true;
                _lastAlertUtc = now;
            }
            else if (_unhealthy)
            {
                _sender.Send("CostPlus poller recovered", "The poller is polling again and no request is waiting too long.");
                _unhealthy = false;
            }
        }
    }

    /// <summary>Checks the poller's health every few seconds and alerts. Off unless the poller is on.</summary>
    internal class CostPlusStallMonitor : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(10);

        private readonly CostPlusHealthCheck _check;
        private readonly CostPlusStallAlerts _alerts;
        private readonly ILogger<CostPlusStallMonitor> _logger;

        public CostPlusStallMonitor(CostPlusHealthCheck check, IStallAlertSender sender, ILogger<CostPlusStallMonitor> logger)
        {
            _check = check;
            _alerts = new CostPlusStallAlerts(sender);
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_check.PollerEnabled) return;
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                try { _alerts.Observe(_check.Check()); }
                catch (Exception ex) { _logger.LogError(ex, "The CostPlus stall monitor could not check or send an alert."); }

                try { await Task.Delay(CheckEvery, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
