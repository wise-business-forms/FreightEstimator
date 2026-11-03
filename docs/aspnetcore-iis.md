# Hosting the ASP.NET Core app under IIS

Port stage 2 (#29, ADR 0005). `FreightEstimator.Web` is the Plant page on ASP.NET Core (.NET 8). It sits beside the .NET Framework app, which is unchanged, until the Plant page has been checked under IIS.

**Hosting is IIS, in-process, through the ASP.NET Core Module. The app is never self-hosted on Kestrel.** `dotnet publish FreightEstimator.Web -c Release` writes the `web.config` the module needs.

On the server:

1. Install the **.NET 8 Hosting Bundle**.
2. Create the site with an app pool set to **No Managed Code**. Per ADR 0005, give it Start Mode AlwaysRunning, Idle Timeout 0 and Preload Enabled (needed once the poller lives in this app; harmless before).
3. Put `ConnectionStrings.Production.json` next to the published files, from `ConnectionStrings.Production.json.example`. It is gitignored and holds the credentials. The app reads it for every environment except Development.
4. Do not set `ASPNETCORE_ENVIRONMENT` on production. IIS then runs the app as Production.

For Debug on a developer machine, copy `ConnectionStrings.Sandbox.json.example` to `ConnectionStrings.Sandbox.json` (also gitignored). The launch profile sets `ASPNETCORE_ENVIRONMENT=Development`, which reads that file, so a Debug run points at SANDBOX and cannot reach production.

Safeguards: a Development run refuses to start unless the `UpsRate` server's name contains "sandbox" (or is named in `FREIGHTESTIMATOR_SANDBOX_SERVER`), whatever supplied the string. Nothing stops a `dotnet run` with no launch profile from running as Production, and reading `ConnectionStrings.Production.json` if one is on the machine, so do not keep that file on a developer machine. Standard ASP.NET Core overrides apply: an environment variable such as `ConnectionStrings__UpsRate` beats the JSON file. `dotnet publish` copies any local `ConnectionStrings.*.json` into the output, so publish from a clean checkout. `wwwroot` holds its own copy of the Bootstrap, jQuery, Modernizr and image files the Framework app serves from `Content` and `Scripts`.

The Plant page is at `/Plant/Index?loc=ALP`. The plant picker (Home), Reports, Rates, Admin and the Quote API are not in this app yet; the "[change plant]" link on the Plant page goes nowhere until the picker is ported.

## Checking it

* `QuoteTests` runs on both targets. On net8.0 it runs the Plant page characterization tests (#15) against this app's `PlantController`, and `PlantPageHostTests` hosts the app in memory and checks routing, static assets, the rendered Plant page, a quote through the pipeline, and the connection-string wiring.
* What those cannot show is IIS itself: load the Plant page from the IIS site once, and submit a quote, before retiring anything.
