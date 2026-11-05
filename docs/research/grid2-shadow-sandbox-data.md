# UPS Ground Freight shadow rows in SANDBOX, as read 2026-11-05

Read-only `SELECT`s against SANDBOX `UpsRate` (`-K ReadOnly`), the steps 1 and 2 queries from `docs/grid2-shadow-review.md` with no `@FixDeployed` cutoff. **This is not ADR 0003 gate evidence.** It is SANDBOX data, not production traffic, and the F5 fix date was not applied.

## Sample

| Rows | Quotes | Plants | First | Last |
|---|---|---|---|---|
| 240 | 240 | 4 | 2026-10-28 16:39 | 2026-11-03 15:17 |

## By plant

| Plant | Rows | Identical | One cent | Larger | Min delta | Max delta |
|---|---|---|---|---|---|---|
| ALP | 44 | 44 | 0 | 0 | 0.00 | 0.00 |
| AND | 19 | 19 | 0 | 0 | 0.00 | 0.00 |
| BUT | 44 | 44 | 0 | 0 | 0.00 | 0.00 |
| FTW | 133 | 133 | 0 | 0 | 0.00 | 0.00 |

## By day

| Day | Rows | Larger |
|---|---|---|
| 2026-10-28 | 26 | 0 |
| 2026-11-03 | 214 | 0 |

## Reading it

* Every row is identical. That matches the audit's expectation that UPS Ground Freight's two formulas agree.
* 214 of the 240 rows were written on 2026-11-03, ending at 15:17:02, which is about 25 seconds before commit `7e77bde` (15:17:27). That pattern looks like test or replay runs, not people quoting. Not confirmed.
* Plants POR and any other active plant have no rows.
* Zero one-cent rows is notable. The audit predicts some from double-precision rounding. It may mean the sample's values never land near a cent boundary, or that the rows came from a path that does not use doubles.
* Not checked: whether `ExistingCalculatedRate = 0` rows exist, and the charges the rows were priced with.
