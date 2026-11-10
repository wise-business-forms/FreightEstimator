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

## The admin area (ADR 0006, #47)

`/Admin/PollerMonitor` (and the **Admin** navbar item) is open only to the users named in the `Admin:AllowedUsers` setting. Reports and the rest of the site are unchanged.

* **Set the users at deploy time**, as `DOMAIN\user` entries, case-insensitive. In `appsettings.Production.json` (or any configuration source): `"Admin": { "AllowedUsers": [ "WBF\\jdoe", "WBF\\pmenefee" ] }`. As an environment variable: `Admin__AllowedUsers__0=WBF\jdoe`, `Admin__AllowedUsers__1=...`. **When the setting is empty or missing, Production denies everyone**, so the page stays closed until someone is named.
* **Turn on Windows Authentication and turn Anonymous Authentication off for the admin path only**, so Reports and the rest of the site stay open to everyone. IIS then asks a visitor to `/Admin` to sign in with their Windows account before the app sees the request. In the site's `web.config`:

  ```xml
  <location path="Admin">
    <system.webServer>
      <security>
        <authentication>
          <anonymousAuthentication enabled="false" />
          <windowsAuthentication enabled="true" />
        </authentication>
      </security>
    </system.webServer>
  </location>
  ```

  The app itself never asks anyone to sign in: anyone who is not listed gets a 403, signed in or not. So if Anonymous Authentication is left on for `/Admin`, nobody is ever prompted and everyone, listed or not, is refused.
* **The Admin navbar item shows only when the browser has already signed in to the site**, and a visitor who has not yet opened `/Admin` is anonymous on the other pages, so the first visit is by the page's address. Whether the browser then sends its credentials on the other pages depends on the IIS and browser setup; check it on the deployed site.
* **Production is IIS only.** The app registers IIS's authentication scheme outside Development, so it is not meant to run on Kestrel in Production (it never does, per the hosting rule above).
* **What the Poller Monitor shows (#49, #50).** Every CostPlus Poller instance that has reported in the last hour, running or stopped; whether the poller is healthy (no successful poll for 60 s, or in the Live stage the oldest unclaimed request over 30 s); the stall monitor's last alert and the probe's last request; and the waiting queue (count, oldest 10, in flight). The page re-reads about every 2 s, and the database is read at most once per 2 s however many browsers are open. It is read-only.
* **Run `Database/FreightEstimator.sql` on the production `FreightEstimator` database before deploying.** It adds `dbo.CostPlusPollerHeartbeat`, where each instance writes a heartbeat every 10 s. Without the table the poller carries on (a failed heartbeat is logged once and dropped), but the Monitor reports that the heartbeats could not be read.
* **A stopped instance after a recycle is expected.** IIS starts the new process before the old one has gone, so for a short while two instances show as running, and the old one then shows as stopped. It stays on the page for an hour after its last heartbeat, and its row is removed after a day.
* **Development skips the check**, so a Debug run needs no setting and no Windows Authentication. The FreightEstimator-only database guard still applies.

The tests cover the access rule and the page through a test sign-in; they cannot show IIS Windows Authentication itself. After deploying, check once from the IIS site: a listed user opens the page, an unlisted user gets 403, and the Admin item is absent for someone who has not signed in.

## Checking it

* `QuoteTests` runs on net8.0. It runs the Plant page characterization tests (#15) against this app's `PlantController`, and `PlantPageHostTests` hosts the app in memory and checks routing, static assets, the rendered Plant page, a quote through the pipeline, and the connection-string wiring.
* What those cannot show is IIS itself: load the Plant page from the IIS site once, and submit a quote, before retiring anything.
