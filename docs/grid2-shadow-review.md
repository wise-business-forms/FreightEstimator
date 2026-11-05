# UPS Ground Freight shadow-row review: the ADR 0003 gate

Checklist and queries for the review that must happen before `UPSGroundFreight` is switched on in the Quote API (#19). ADR 0003: a call site goes live only once a reasonable sample of its divergences has been reviewed and **every one is explained**. Nothing is explained by default.

Run everything below against the production `UpsRate` database, read-only. These are `SELECT`s; nothing here writes.

## What the rows are

`dbo.CalculatedRateShadowLog` (`Database/002`), one row per UPS Ground Freight quote. The code stores its name as `CallSite = 'GRID2'`, which is why the queries below use that literal:

| Column | Meaning |
|---|---|
| `ExistingCalculatedRate` | what the Plant page's legacy path showed (a `double` sum, stored as `DECIMAL(18,2)`) |
| `NewCalculatedRate` | what `CalculatedRate.Apply` produced for the same Carrier Rate |
| `Delta` | `New - Existing` |

The row does **not** store the charges used. The legacy side's charges are in `dbo.QuoteServiceCalculation` for the same `QuoteId` (`CarrierRate`, `MarkupPercent`, `PerPackageCharge`, `PerShipmentCharge`). The new side's charges are whatever the production Plant Charges hold now, so re-read them at review time (step 4).

## Expected result

The audit (`docs/research/calculation-audit.md`, 4.4.3 and the divergence table) says the two formulas are identical for UPS Ground Freight, so the expected delta is **0**. Any non-zero delta should come from one of:

* **F5**, the charge lookup mismatch (fixed in `f99ab48`, so it should now be gone). Before the fix, the legacy side read markup and fixed charges from different carrier rows than the new side.
* **F6**, a data-source difference between the legacy and new lookups.
* **T1**, double precision. A live value such as `120.99999999999999` is stored as 120.99 or 121.00, so expect some **±$0.01** deltas that are artifacts, not divergences.

A delta of any other size or cause is **unexplained**, and the gate fails until it is.

## Step 0: pick the window

Rows written before the F5 fix reached production are not evidence. Set the cutoff to the moment the fix was deployed.

```sql
DECLARE @FixDeployed DATETIME = '2026-MM-DD HH:MM';   -- fill in: when the F5 fix went live
```

If the fix has not been deployed, stop. There is nothing usable to review yet.

## Step 1: is the sample big enough?

```sql
SELECT COUNT(*)                          AS Rows,
       COUNT(DISTINCT QuoteId)           AS Quotes,
       COUNT(DISTINCT PlantCode)         AS Plants,
       MIN(CreatedDate)                  AS First,
       MAX(CreatedDate)                  AS Last
FROM dbo.CalculatedRateShadowLog
WHERE CallSite = 'GRID2' AND CreatedDate >= @FixDeployed;
```

"Reasonable" is a judgment, so record it. As a floor, cover every active plant that quotes Ground Freight, and a spread of package counts (1, 2-5, 6+) and weights. If a plant has no rows, it is not covered; note it in the finding.

## Step 2: how many diverge, and by how much?

```sql
SELECT PlantCode,
       COUNT(*)                                        AS Rows,
       SUM(CASE WHEN Delta = 0 THEN 1 ELSE 0 END)      AS Identical,
       SUM(CASE WHEN ABS(Delta) = 0.01 THEN 1 ELSE 0 END) AS OneCent,
       SUM(CASE WHEN ABS(Delta) > 0.01 THEN 1 ELSE 0 END) AS Larger,
       MIN(Delta) AS MinDelta, MAX(Delta) AS MaxDelta
FROM dbo.CalculatedRateShadowLog
WHERE CallSite = 'GRID2' AND CreatedDate >= @FixDeployed
GROUP BY PlantCode
ORDER BY PlantCode;
```

* **Identical**: nothing to explain.
* **OneCent**: probably T1. Do not accept it blindly (step 3), because a real one-cent divergence would hide here.
* **Larger**: every row needs an explanation (step 4).

## Step 3: check that one-cent rows really are rounding

A genuine T1 artifact comes from a legacy value that is a hair under or over a cent boundary. Recompute from the logged components and compare:

```sql
SELECT s.QuoteId, s.PlantCode, s.ExistingCalculatedRate, s.NewCalculatedRate, s.Delta,
       q.CarrierRate, q.MarkupPercent, q.PerPackageCharge, q.PerShipmentCharge,
       -- recomputed to full precision from what the legacy side logged
       q.CarrierRate + q.CarrierRate * q.MarkupPercent / 100.0 + q.PerShipmentCharge AS BeforePerPackage
FROM dbo.CalculatedRateShadowLog s
JOIN dbo.QuoteServiceCalculation q
  ON q.QuoteId = s.QuoteId AND q.Carrier = 'UPS' AND q.ServiceName = s.ServiceName
WHERE s.CallSite = 'GRID2' AND s.CreatedDate >= @FixDeployed AND ABS(s.Delta) = 0.01;
```

Note the logged `CarrierRate` is cut to two decimals (flagged in #19), so recomputing may itself differ by a cent. Treat a one-cent row as explained only if the legacy value is a cent boundary case of the formula (the sum lands within a fraction of a cent of `x.xx5`). If it does not, treat it as **larger** and explain it in step 4.

## Step 4: explain every larger delta

```sql
SELECT s.QuoteId, s.CreatedDate, s.PlantCode, s.ServiceName,
       s.ExistingCalculatedRate, s.NewCalculatedRate, s.Delta,
       q.CarrierRate, q.MarkupPercent, q.PerPackageCharge, q.PerShipmentCharge
FROM dbo.CalculatedRateShadowLog s
LEFT JOIN dbo.QuoteServiceCalculation q
  ON q.QuoteId = s.QuoteId AND q.Carrier = 'UPS' AND q.ServiceName = s.ServiceName
WHERE s.CallSite = 'GRID2' AND s.CreatedDate >= @FixDeployed AND ABS(s.Delta) > 0.01
ORDER BY ABS(s.Delta) DESC;
```

For each row, work out which input differs.

1. Get the Plant Charges the new module reads for that plant now: the `GF` carrier row for markup, per-package and per-shipment (the audit's canonical row for UPS Ground Freight is "markup `GF`, fixed `UPS`", so confirm against `RateCalculationsChargeSchedule` which carrier each value comes from in the deployed build).
2. Compare them to the `MarkupPercent`, `PerPackageCharge`, `PerShipmentCharge` the legacy side logged.
3. If they differ, **check whether the charges changed between the quote and now** (`CreatedDate` vs the Plant Charges history). A quote made before a rate change will diverge because the new side priced against today's charges, not the quote-time charges. That explains the row, but it is a review artifact and not a defect.
4. If the charges match and the delta is still large, it is unexplained. Do not proceed.

Record each as one of: `T1`, `F6`, `charges-changed-since-quote`, or `UNEXPLAINED`.

## Step 5: also check what the divergence query cannot see

* **Missing rows.** A shadow write that failed is swallowed (`ShadowComparisonRecorder` traces a warning and carries on). Compare counts of Ground Freight Quotes to shadow rows:

  ```sql
  SELECT COUNT(*) AS GroundFreightQuotes
  FROM dbo.QuoteServiceCalculation
  WHERE Carrier = 'UPS' AND ServiceName = 'UPSGroundFreight' AND CreatedDate >= @FixDeployed;
  ```

  A large gap versus step 1's `Rows` means the sample is biased towards quotes where logging worked. Check the app's trace log for `ShadowComparisonRecorder.Record failed for GRID2`.
* **Zero or missing carrier rates.** A legacy `0.0` for a missing UPS value will match a new `0` and look identical. Look for rows with `ExistingCalculatedRate = 0`.

## Step 6: write the finding

Attach to ADR 0003's UPS Ground Freight gate. Template:

```
UPS Ground Freight shadow review, <date>, window <FixDeployed> to <last row>
Rows: N   Quotes: N   Plants covered: <list>   Plants not covered: <list>
Identical: N   One-cent: N (all T1 / exceptions listed)   Larger: N
Larger deltas, each explained:
  <QuoteId> <plant> <delta>: <T1 | F6 | charges-changed-since-quote>: <evidence>
Unexplained: none   (or list, and the gate does NOT pass)
Missing-row check: Quotes N vs shadow rows N
Verdict: PASS / FAIL
```

## After a pass

1. Set the appSetting `QuoteApi.AvailableServices` to `UPSGroundFreight` in production. It is off by default.
2. Confirm `Database/005` (`Consumer`, `Quote`) has been run there (#18).
3. Update #19 and #3 with the finding, and close #19.
4. Do not change the Plant page. It stays on its own formula until its own cutover.

## Also covered by the replay harness

`docs/replay-harness.md` (#26) replays stored UPS responses through both formulas in SANDBOX and classifies every difference, which gives the same evidence without waiting for live traffic. The shadow rows above are still the gate evidence for production, since the replay prices against SANDBOX's Plant Charges.
