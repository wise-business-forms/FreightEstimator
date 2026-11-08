using AuthenticationServer.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

var builder = WebApplication.CreateBuilder(args);

// Data connection strings are per environment, and the credentials are kept out of the repo (see
// ConnectionStrings.Sandbox.json.example): Development reads the SANDBOX file, every other environment the
// Production one. A missing file is not an error here; the first connection says which string is not configured.
// Debug runs are Development, so they cannot reach production. IIS leaves the environment at Production unless
// ASPNETCORE_ENVIRONMENT says otherwise, so a published site is production by default.
string connectionStringsFile = builder.Environment.IsDevelopment() ? "ConnectionStrings.Sandbox.json" : "ConnectionStrings.Production.json";
builder.Configuration.AddJsonFile(connectionStringsFile, optional: true, reloadOnChange: false);

builder.Services.AddControllersWithViews();

// The CostPlus poller (ADR 0005): a background service in this app, off unless CostPlus:Poller:Enabled is true.
builder.Services.AddSingleton<AuthenticationServer.CostPlus.CostPlusPollerHealth>();
builder.Services.AddSingleton<AuthenticationServer.CostPlus.CostPlusHealthCheck>();
builder.Services.AddSingleton<AuthenticationServer.CostPlus.IStallAlertSender, AuthenticationServer.CostPlus.SmtpStallAlertSender>();
builder.Services.AddHostedService<AuthenticationServer.CostPlus.CostPlusPollerService>();
builder.Services.AddHostedService<AuthenticationServer.CostPlus.CostPlusStallMonitor>();   // alerts on a stall (#35)
builder.Services.AddHostedService<AuthenticationServer.CostPlus.CostPlusProbeService>();   // quiet-hours probe request (#35)

WebApplication app = builder.Build();

IConfiguration configuration = app.Configuration;
Configuration.ConnectionStringSource = name => configuration.GetConnectionString(name);

if (app.Environment.IsDevelopment())
{
    // A Development (Debug) run must be on the FreightEstimator database. Whatever file or setting supplied the strings,
    // refuse to start unless the database is FreightEstimator. UpsRate is never allowed.
    var connection = new System.Data.SqlClient.SqlConnectionStringBuilder(Configuration.FreightEstimatorSqlConnection);
    if (!string.Equals(connection.InitialCatalog, "FreightEstimator", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Refusing to start in Development: the database is '" + connection.InitialCatalog + "', not FreightEstimator. UpsRate is never a development target.");
}
else
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync("<!DOCTYPE html><html><head><title>Error</title></head><body><h1>Error.</h1><h2>An error occurred while processing your request.</h2></body></html>");
    }));
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "Default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Health of the CostPlus poller (#35): 503 past the stall thresholds, for a scheduled check or monitor to call.
app.MapGet("/health/costplus-poller", AuthenticationServer.CostPlus.CostPlusHealthCheck.Handle);

app.Run();

/// <summary>Lets the test projects host the app in memory.</summary>
public partial class Program { }
