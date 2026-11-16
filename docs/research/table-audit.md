# Table audit: database objects the app uses

Audited 2026-11-16 on branch `port-stage-2`. Static read of `FreightEstimator.Core`, `FreightEstimator.Web` and `Database/*.sql`. Nothing was run against any database, so the live schema is unchecked. Test projects (`Tests`, `QuoteTests`) are excluded except where noted.

## Databases the app connects to

| Connection | Database | Access | Used by |
|---|---|---|---|
| `FreightEstimator` | FreightEstimator (sandbox: AZUREDB01; production server per `ConnectionStrings.Production.json.example`) | read and write | almost everything |
| `UpsRateReadOnly` | UPSRate (production) | read only, enforced by a regex guard in `QuoteLogReport.Select` | Quote Log (ADR 0007) |
| `Datawarehouse` | Datawarehouse, plus linked server `ALPERP` | read | Margin Report |
| `WiseLink` | WBFDOTCOM | none found | property exists in `Configuration.cs`, no caller |

## Tables in the FreightEstimator database

R = reads, W = writes. "via proc" means through a stored procedure.

| Table | Access | Where used | Purpose |
|---|---|---|---|
| `Plants` | R | `Plant.cs`, `PlantDirectory.cs`, `LogisticsPartnerEstimationService.cs:378`, proc `RecordRateRequestPlants` | Plant addresses, UPS shipping numbers. Rows come from the seed script only. |
| `PlantCharges` | R | `Plant.cs:100`, proc `GetPlantCharges` | Per-plant carrier charges. Name carries a year. |
| `PiedmontAcctsForSpecialAlpRates` | R via proc | `GetPlantCharges` | Special ALP rate accounts. Columns inferred, not read from production. |
| `Requests_Rate` | W via proc `LogRequest_Rate`; R | `Log.cs:295`, `ReportsController` (count, page), procs `RecordRateRequestPlants` and `RecordRateRequestMatchKeys` | Request and response log for every UPS quote. |
| `Log_LTL` | W via proc `LogResultsLTL` | `LogisticsPartnerEstimationService.cs:666` | LTL request and result log. Never read by the app. Columns inferred. |
| `QuoteServiceCalculation` | W via proc; R | `Log.cs:355`, `RateChangeData.cs:92` | Persisted Calculated Rate per service. |
| `CalculatedRateShadowLog` | W via proc | `CalculatedRateShadowLog.cs` | Shadow comparison of the unified rate module (ADR 0003). Never read by app code. |
| `RateRequestPlant` | W via proc; R | `ReportsController.cs:172`, proc `RecordRateRequestPlants` | Request-to-plant map, a derived table. |
| `RateRequestMatchKey` | W via proc; R | `RateChangeData.cs` (4 queries) | Derived match keys for the Rate Changes report. |
| `RateRequestServiceRate` | W via proc; R | `RateChangeData.cs:116` | Per-service rates parsed from the response JSON. |
| `Consumer` | R | `ConsumerStore.cs`, `QuoteLog.cs:60`, `CostPlusPollCycle.cs:388` | API consumers and key hashes. Seed rows: CostPlus, plus WAFE from `006`. |
| `Quote` | W via proc `LogQuote` | `QuoteLog.cs` | Quote ID, reference number, request JSON. Never read by app code. |
| `FreightRequests` | R, W | `CostPlusPollCycle*.cs`, `CostPlusQueue.cs`, `CostPlusProbe.cs`, `ShadowRunReport.cs` | CostPlus request queue. Polled, updated with results, probe rows inserted. |
| `CostPlusRequestClaim` | R, W | `CostPlusPollCycle.cs`, `CostPlusQueue.cs` | Lease claims so a row is not processed twice. |
| `CostPlusShadowResult` | R, W | `CostPlusPollCycle.Shadow.cs`, `ShadowRunReport.cs` | Console vs poller comparison, with explanations. |
| `CostPlusPollerHeartbeat` | R, W (incl. DELETE) | `CostPlusHeartbeatStore.cs` | One row per poller instance. |
| `CostPlusPollerEvent` | R, W (incl. DELETE) | `PollerEventStore.cs` | Poller Events, pruned by age. |
| `CostPlusReferenceNumber` | sequence, not a table | `CostPlusPollCycle.cs:399` | Reference number source. |

## Tables outside the FreightEstimator database

| Object | Database | Access | Where | Note |
|---|---|---|---|---|
| `Requests_Rate` | UPSRate (production) | read only | `QuoteLogReport.cs:56,69` | The only production table the app touches. Both statements are SELECTs. |
| `Freight_Margin_VIEW` | Datawarehouse | read | `ReportsController.cs:36` | Known-dead leftover. The failure is caught and shown as "data unavailable". |
| `ALPERP.PSLWise.dbo.ARSAL_T1` | ERP linked server | read | `ReportsController.cs:37` | Joined with the view above, so it fails with it. |

## Findings

1. **`Test_Paul` does not exist in the schema.** `Test.cs:25` inserts into `dbo.Test_Paul` with an empty `VALUES ()`, which is invalid SQL. Nothing calls `CheckQueue` or `processNextRequest`. This is dead scratch code and can be deleted.
2. **`GetEmployeeById` has no definition in `Database/`.** `Data/Request.cs:18` calls it and nothing calls that class. It is dead code that points at a procedure the repo does not define.
3. **Write-only tables.** `Log_LTL`, `Quote` and `CalculatedRateShadowLog` are written but no app code reads them. They are probably read by reports or by people querying directly, so confirm before treating them as unused.
4. **Interpolated SQL in `Plant.cs`.** Lines 33 and 100 and `LogisticsPartnerEstimationService.cs:378` concatenate `PlantCode` or `plantID` into SQL text. If those values can come from request input, they are injectable. They should be parameterised. I did not trace the callers.
5. **`SELECT *` on `PlantCharges` and `FreightRequests`** (`Plant.cs:100`, `CostPlusPollCycle.Shadow.cs:176`, `ShadowRunReport.cs:313`). A column change can alter behaviour silently.
6. **`Database/000_sandbox_baseline.sql:225` contains a three-part `[UpsRate].[dbo].[Log_LTL]` name** in the `LogResultsLTL` procedure. `FreightEstimator.sql` removes it. If `000` is still run anywhere, that procedure writes to UPSRate. Consider retiring `000`.
7. **`FreightRequests` has no primary key** in `FreightEstimator.sql` (the script notes it was not read from the source). The `CostPlusRequestClaim` table is the only guard against double-processing.
8. **Unused `WiseLink` connection.** The connection string is declared and exposed but nothing uses it.
9. **Derived tables depend on stored-procedure catch-up.** `RateRequestPlant`, `RateRequestMatchKey` and `RateRequestServiceRate` are filled by `RecordRateRequestPlants` and `RecordRateRequestMatchKeys` when a report runs. A GET on a report page therefore writes to the database. This is by design but worth knowing.

## Method and limits

- Searched `.cs` and `.cshtml` for SQL keywords, `SqlCommand` and `CommandText` uses, and `CommandType.StoredProcedure` calls. Then read the stored-procedure bodies in `Database/FreightEstimator.sql` to attribute their table access.
- Not covered: tables referenced only inside the test projects (the sandbox helpers create and clear rows in the tables above), and any object that exists in production but not in these scripts. A live comparison with `Database/schema-fingerprint.sql` would close that gap.
