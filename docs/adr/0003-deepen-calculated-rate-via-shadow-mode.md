# Deepen Calculated Rate into one module, migrated via shadow mode

**Status:** accepted

Four call sites compute Calculated Rate independently and disagree (`docs/issues/0001`): UPS GRID1 single-plant only marks up CWT-eligible shipments, UPS GRID1 multi-plant applies no markup at all (the line is commented out), UPS GRID2 has a dead CWT branch and only marks up one service name, and LTL has its own fourth formula. None of this is testable today without a live SQL connection, since `RateCalculations` opens its own `SqlConnection` in its constructor.

We're consolidating all four into one pure module, `CalculatedRate.Apply(CarrierRate, ChargeSchedule, RatingContext)`, using one canonical formula applied unconditionally (no CWT-gating, no service-name-gating): `CalculatedRate = CarrierRate + CarrierRate × MarkupPercent + PerPackageCharge × PackageCount + PerShipmentCharge`. `ChargeSchedule` is a narrow seam — a production SQL adapter and an in-memory test adapter — around just the Plant Charges lookups this module needs, which is what makes it testable (new xUnit test project, TDD). Migration happens one call site at a time, starting with GRID2 (smallest, most isolated, one live caller), and each migration ships in **shadow mode**: the new module computes alongside the existing per-call-site formula, both results are written to a throwaway comparison table, and the live quote calculation and UI keep showing today's value. A call site only flips from shadow to live once a reasonable sample of its divergences has been reviewed and every one is explained by the known bug — not on a time or volume schedule.

## Considered options

- **Fix all four call sites live immediately**, since the bug affects the price shown today. Rejected: this touches the live quote calculation/UI, and per standing project practice, reporting-driven work does not change that path, even to fix a bug it finds. Shadow mode lets the fix be proven against real traffic without breaking that rule.
- **Full rewrite of `RateCalculations`'s and `Plant.cs`'s DB access behind `ChargeSchedule`**, as originally proposed. Rejected as broader than this fix needs — only the Plant Charges lookups `CalculatedRate.Apply` actually reads move behind the seam.
- **Fix CWT eligibility (the billing-weight over-count bug) as part of the same work**, since GRID2's dead CWT branch sits right next to it. Rejected: CWT eligibility is an independent, already-identified bug (it can flip a shipment onto the wrong pricing basis) with its own blast radius. This module accepts the CWT flag as given.

## Consequences

- `docs/issues/0001` is broadened from "UPS Calculated Rate computed three different ways" to cover all four call sites, including LTL, and closes only once all four are live on the new module — not just after GRID2.
- Until every call site is migrated and flipped, Quotes still show today's inconsistent, under-marked-up price. This ADR intentionally does not fix the user-visible bug yet — only builds and proves the replacement.
