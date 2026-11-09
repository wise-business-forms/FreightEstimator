# Hosting the ASP.NET Core app under IIS

Port stages 2 and 3 (#29, #30, ADR 0005). `FreightEstimator.Web` is the whole web app on ASP.NET Core (.NET 8): the plant picker, the Plant page, Reports and the Quote API. It replaced the .NET Framework (System.Web) app, which has been deleted.

**Hosting is IIS, in-process, through the ASP.NET Core Module. The app is never self-hosted on Kestrel.** `dotnet publish FreightEstimator.Web -c Release` writes the `web.config` the module needs.

On the server:

1. Install the **.NET 8 Hosting Bundle**.
2. Create the site with an app pool set to **No Managed Code**. Per ADR 0005, give it Start Mode AlwaysRunning, Idle Timeout 0 and Preload Enabled (needed once the poller lives in this app; harmless before). The commands, the 10-second fallback check and the Windows Service fallback are in `docs/costplus-poller-iis.md`.
3. Put `ConnectionStrings.Production.json` next to the published files, from `ConnectionStrings.Production.json.example`. It is gitignored and holds the credentials. The app reads it for every environment except Development.
4. Do not set `ASPNETCORE_ENVIRONMENT` on production. IIS then runs the app as Production.

For Debug on a developer machine, copy `ConnectionStrings.Sandbox.json.example` to `ConnectionStrings.Sandbox.json` (also gitignored). The launch profile sets `ASPNETCORE_ENVIRONMENT=Development`, which reads that file, so a Debug run points at SANDBOX and cannot reach production.

Safeguards: a Development run refuses to start unless the `FreightEstimator` connection string's database is `FreightEstimator`, whatever supplied the string. The test database is `FreightEstimator` on `AZUREDB01\AZUREDB01`, created by `Database/FreightEstimator.sql`; `UPSRate` on the same server is never a target. The QuoteTests guard applies the same rule. Nothing stops a `dotnet run` with no launch profile from running as Production, and reading `ConnectionStrings.Production.json` if one is on the machine, so do not keep that file on a developer machine. Standard ASP.NET Core overrides apply: an environment variable such as `ConnectionStrings__FreightEstimator` beats the JSON file. `dotnet publish` copies any local `ConnectionStrings.*.json` into the output, so publish from a clean checkout. Static assets (Bootstrap, jQuery, Modernizr, images) are in `wwwroot`.

The site root is the plant picker (`/`). The Plant page is at `/Plant/Index?loc=ALP`, Reports at `/Reports`, and the Quote API is `POST /api/v1/quotes` and `GET /api/v1/plants` (`X-Api-Key` header). The Rates and Log stubs, the Values sample API, the Admin stubs and the Web API Help Page were dropped, not ported.

## Checking it

* `QuoteTests` runs on net8.0. It runs the Plant page characterization tests (#15) against this app's `PlantController`, and `PlantPageHostTests` hosts the app in memory and checks routing, static assets, the rendered Plant page, a quote through the pipeline, and the connection-string wiring.
* What those cannot show is IIS itself: load the Plant page from the IIS site once, and submit a quote, before retiring anything.
