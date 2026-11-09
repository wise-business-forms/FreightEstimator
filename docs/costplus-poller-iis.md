# CostPlus poller under IIS: settings, the 10-second check, and the Windows Service fallback

ADR 0005 puts the CostPlus poller in this app (a `BackgroundService`), hosted by IIS. This is the deployment note for that decision (#37). For the site itself, see `docs/aspnetcore-iis.md`.

## 1. App pool settings

The poller stops whenever IIS idles or unloads the app, and CostPlus just waits silently. Set all three on the app pool that runs this site:

| Setting | Value | Why |
|---|---|---|
| Start Mode | `AlwaysRunning` | IIS starts the worker process when the service starts, not on the first web request |
| Idle Time-out (minutes) | `0` | The pool is never shut down for having no web traffic |
| Preload Enabled (on the **site**) | `True` | IIS sends a warm-up request after start, so the app (and the poller) starts without a visitor |

Preload Enabled is a property of the site (application), not the pool. From an elevated prompt (replace `FreightEstimator` with the real pool and site names):

```
%windir%\system32\inetsrv\appcmd set apppool /apppool.name:"FreightEstimator" /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00 /managedRuntimeVersion:""
%windir%\system32\inetsrv\appcmd set app "FreightEstimator/" /preloadEnabled:true
```

Check them:

```
%windir%\system32\inetsrv\appcmd list apppool "FreightEstimator" /text:startMode
%windir%\system32\inetsrv\appcmd list apppool "FreightEstimator" /text:processModel.idleTimeout
%windir%\system32\inetsrv\appcmd list app "FreightEstimator/" /text:preloadEnabled
```

Also: Preload needs the Application Initialization IIS feature installed, and a pool recycle still restarts the poller (an overlapped recycle briefly runs two copies, which the claim, the lease and the stall alert are built for; ADR 0005). Prefer a recycle at a fixed quiet-hours time over the default 29-hour interval.

Then turn the poller on in the published `appsettings.Production.json` (or the environment): `CostPlus:Poller:Enabled = true`. It runs in `Shadow` mode (observe only) until `CostPlus:Poller:Mode` is set to `Live`, which happens only at the cutover (#38).

> **Status:** these settings are documented here; applying them on the server is a manual step. Tick it off in the log in section 3 once it is done and verified.

## 2. The 10-second, three-in-a-row rule

**The rule (ADR 0005):** the poller has failed under IIS if it cannot pick up a request and write a result back within 10 seconds, three times in a row. Then move to the Windows Service in section 4.

"Within 10 seconds" is `DateRequested` to `DateRated` on the request row. Two kinds of request count:

* **Real requests**, answered in `Live` mode. These include the UPS call, so a slow UPS counts against the poller. UPS averaged 1.8 s (max 3.3 s) in `CarrierApiCall`, so a real UPS-only request has plenty of slack.
* **Probe requests** (LoginId starts with `CostPlusProbe-`), inserted every 5 minutes between 22:00 and 05:00 so there is data when nobody is asking. A probe makes no UPS call, so it tests only claim and write-back, which is the part IIS could break.

**This rule is not automated.** The code does not count consecutive slow answers. What the code does do is alert on stalls (#35): oldest unclaimed request over 30 s, or no successful poll for 60 s, with an email to pmenefee@wbf.com. A stall alert is the signal to run the check below. Someone also runs it each Monday morning during the first month after cutover.

### The check

Run on the server that holds `FreightRequests` (the table is the CostPlus one, reached from `UpsRate` through the linked server; read-only query):

```sql
-- The last 30 answered requests, newest first, with how long each took and whether it broke the 10-second rule.
SELECT TOP (30)
       f.LoginId, f.QtyNumber, f.DateRequested, f.DateRated,
       DATEDIFF(MILLISECOND, f.DateRequested, f.DateRated) / 1000.0 AS Seconds,
       CASE WHEN DATEDIFF(MILLISECOND, f.DateRequested, f.DateRated) > 10000 THEN 'SLOW' ELSE 'ok' END AS Verdict,
       CASE WHEN f.LoginId LIKE 'CostPlusProbe-%' THEN 'probe' ELSE 'real' END AS Kind
FROM dbo.FreightRequests f
WHERE f.DateRated IS NOT NULL
ORDER BY f.DateRated DESC;
```

Probe rows are deleted an hour after they were answered, so for an older probe run, look at the health endpoint and the log instead.

Read the `Verdict` column in time order. **Three `SLOW` in a row, with no `ok` between them, is a failure.** A single slow answer is not. A request still unanswered after 10 seconds counts as slow: check `GET /health/costplus-poller`, whose `oldestUnclaimedRequestAgeSeconds` shows it.

Before blaming IIS, rule out the other causes:

* Real requests only: UPS or the Logistics Partner was slow (compare with `CarrierApiCall`).
* The health endpoint is `unhealthy` because the request table could not be read (network, linked server, database).
* The pool was recycled in that window (IIS event log, source `WAS`).

If it is IIS (the pool idled, was recycled or starved the background service), go to section 4.

## 3. Record of checks

Add a row each time the check is run, or the rule fires. This is the place someone reads to see how the poller has behaved.

| Date | Who | Kind (settings applied / weekly check / alert follow-up) | Result (slowest of last 30, any three-in-a-row?) | Action |
|---|---|---|---|---|
| | | | | |

## 4. Fallback: run the poller as a Windows Service

The fallback is a second deployable that shares the estimation library, not a rewrite. The poller classes live in `FreightEstimator.Web/CostPlus` today and the estimation code in `FreightEstimator.Core`.

1. **Make a worker project.** Add `FreightEstimator.Poller` (`Microsoft.NET.Sdk.Worker`, net8.0) to the solution. Reference `FreightEstimator.Core`, and move or link the `CostPlus` poller classes (`CostPlusPollerService`, `CostPlusPollCycle`, `CostPlusQueue`, `CostPlusStallMonitor`, `CostPlusProbeService` and the rest) so both hosts use the same files. Do not copy them.
2. **Host it as a service.** Add the `Microsoft.Extensions.Hosting.WindowsServices` package and call `builder.Services.AddWindowsService(o => o.ServiceName = "FreightEstimatorPoller")`. Register the same hosted services and configuration as `Program.cs` does, and the same `ConnectionStrings.Production.json`. Expose the health endpoint (a minimal Kestrel listener on an internal port is acceptable here, since this is a separate process from the IIS site; the "never self-hosted on Kestrel" rule in `docs/aspnetcore-iis.md` is about the web app) so the scheduled external check still works.
3. **Turn the poller off in IIS first.** Set `CostPlus:Poller:Enabled = false` for the site and recycle the pool. Two hosts claiming rows is safe (the claim is atomic) but doubles the load, and the health endpoint of the site would report `disabled`.
4. **Publish and install.**
   ```
   dotnet publish FreightEstimator.Poller -c Release -o C:\Services\FreightEstimatorPoller
   sc.exe create FreightEstimatorPoller binPath= "C:\Services\FreightEstimatorPoller\FreightEstimator.Poller.exe" start= delayed-auto
   sc.exe failure FreightEstimatorPoller reset= 86400 actions= restart/5000/restart/5000/restart/60000
   sc.exe start FreightEstimatorPoller
   ```
   Run it as a service account that can read and write `FreightRequests` and the `FreightEstimator` database, not as a person.
5. **Verify.** `GET` the health endpoint (`healthy`, a recent `lastSuccessfulPollUtc`), wait for a quiet-hours probe or insert one, and run the check in section 2. Log it in section 3.
6. **Update the records.** Note the move in ADR 0005 (it already names this as the fallback), in `docs/aspnetcore-iis.md` and in the log above, and point the scheduled external health check at the service's address.

The cutover rules do not change: Live mode, the console app skipping UPS-only rows, and the shadow-run sign-off (#38) apply whichever host runs the poller.
