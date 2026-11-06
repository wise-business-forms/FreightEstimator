# Replay harness: evidence for the ADR 0003 cutover gate

Replays stored carrier responses through today's formula and through the unified `CalculatedRate.Apply`, and reports where they differ, so the gate ("every divergence is explained", ADR 0003) can be judged without waiting for live traffic. Built for #26.

## What it does

For each stored call it rebuilds the request, answers the carrier with the stored response (the carrier HTTP is faked, so **no carrier is called**), and prices every service twice, against the Plant Charges currently in SANDBOX:

* **Legacy**: the real legacy path, `UpsEstimationService.Quote` / `LogisticsPartnerEstimationService.Quote`, as the Plant page runs it.
* **Unified**: `EstimateParcel` and `EstimateGroundFreight` (the Quote API's path) for the UPS call sites, and `CalculatedRate.Apply` directly, per option, for LTL (the API's LTL view is the cheapest option only), rounded half-up to the cent (#13).

| Call site | Stored in | Compared |
|---|---|---|
| `GRID1Single` (UPS parcel) | `Requests_Rate`, a call with `PaymentDetails` | each service that carries Plant Charges |
| `GRID2` (UPS Ground Freight) | `Requests_Rate`, a call with `FRSPaymentInformation` | the one service |
| `LTL` | `Log_LTL` | each Logistics Partner option (not the API's cheapest-only view) |

Each difference is classified (`QuoteTests/Replay/ReplayReport.cs`):

* **Identical**: no difference.
* **RoundingNoise**: within a cent. Legacy values are unrounded or truncated; the unified side rounds half-up.
* **KnownCause**: GRID1 non-CWT services, where the whole difference is the markup that path leaves off and #14 decided is intended (for example plant AND's Ground and 3 Day Select). The rule is not limited to a plant or service: any GRID1 non-CWT difference that equals the markup is accepted.
* **Unexplained**: anything else. The report lists these first, with the stored call each came from. **The gate fails for a call site while any exist, or while it has no stored responses at all.**

Adding a new known cause means a decision on record (an issue or ADR) and a new rule in `DeltaClassifier`, with a test. Nothing is explained by default.

## Running it

1. **Human step: copy a sample of stored calls into SANDBOX.** `Requests_Rate` and `Log_LTL` rows from production hold customer addresses, so the harness does not do this. Copy the rows into the SANDBOX `UpsRate` database (the newest few hundred of each is plenty), and make sure SANDBOX `Plants` and `PlantCarrierCharges2017` hold the real Plant Charges the comparison should use. The harness prices against whatever Plant Charges SANDBOX holds; it does not set its own.
2. Build the test project (see the commands in the repo's README or `docs/agents/`) and run the explicit test:

   ```
   set QUOTETESTS_REPLAY_SAMPLE=500
   set QUOTETESTS_REPLAY_REPORT=C:\path\to\replay-report.md
   dotnet test QuoteTests\bin\Debug\net8.0\FreightEstimator.QuoteTests.dll --filter "Name=ReplayStoredResponsesAndWriteTheReport"
   ```

   `QUOTETESTS_REPLAY_SAMPLE` is how many stored calls of each kind to read, newest first (default 500). Without `QUOTETESTS_REPLAY_REPORT` the report goes to the test's output.
3. Read the report. Each call site has a one-line gate verdict, a table grouped by plant and service (rows, min and max delta, outcomes, why), then every service with its legacy value, unified value and delta, unexplained ones first. Calls that were read but could not be replayed are listed under "Not replayed" with the reason (for example a ship-from that matches no active plant); none are dropped silently.
4. Attach the section for each call site to ADR 0003's gate for that path as its written finding.

## Safety

* It refuses to run unless the configured `UpsRate` connection is recognisably SANDBOX (`SandboxGuard`), so it cannot read or write production.
* The legacy path logs as it always has (`Requests_Rate`, `Log_LTL`, `QuoteServiceCalculation`, `CalculatedRateShadowLog`). After a run the harness deletes the rows it caused, in SANDBOX: `Requests_Rate` and `Log_LTL` rows added since the run began, and per-Quote rows for the Quote IDs it made up. Do not run it while something else is writing to SANDBOX's `Requests_Rate`, or those rows are removed too.

## Limits

* The address check is not stored, so a recorded "Commercial" answer is used. It does not change a price.
* GRID1 multi-plant is not replayed (no markup is applied there at all; it is not yet migrated).
* One cent is called rounding noise whichever way it goes, so a real one-cent divergence would be filed there.
* A stored UPS call is matched to a plant by its ship-from city and state, as the Past Quotes report does.
