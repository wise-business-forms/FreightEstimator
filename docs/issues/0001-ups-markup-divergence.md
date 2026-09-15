# UPS Calculated Rate is computed three different ways, depending on which grid produced it

**Found while:** designing quote-history persistence (see `docs/adr/0002-app-generated-quote-id.md`).
**Status:** not fixed — logged as-is per the decision in that design conversation. Needs its own investigation and fix, separate from any reporting work.

`Controllers/PlantController.cs` has three independent code paths that each compute UPS's final marked-up price for a service, and nothing guarantees they agree for the same shipment/service:

1. **GRID 1, single-plant mode** (`ShipmentConfirmation`, single-location branch): markup applied inline in `ApplyPlantCharges` (`PlantController.cs:507-583`). For the CWT/negotiated case: `CustomerRate = NegotiatedRateCharges_TotalCharge + (NegotiatedRateCharges_TotalCharge * Plant_Surcharge / 100)`, then per-package/per-shipment charges added (`PlantController.cs:572-575`).
2. **GRID 1, multi-plant ("rate from multiple locations") mode**: `CustomerRate` is **never marked up at all** — the multi-plant loop (`PlantController.cs:214-291`) computes CWT eligibility and plant charge lookups but never applies them to `CustomerRate`. An attempt to call `RateCalculations.CalculateUPSRate` here is commented out (`PlantController.cs:217`). This path silently shows the raw carrier rate as if it were the final price.
3. **GRID 2 (ground freight only)**: calls the separate static `RateCalculations.CalculateUPSRate` (`Models/Services/RateCalculations.cs:201`), a third, differently-structured markup implementation, invoked only from `PlantController.cs:435`.

Three formulas for what's conceptually the same operation ("apply our markup to a carrier rate") means the same UPS Ground quote could show a different final price depending on which grid the user looked at — including, in the multi-plant case, no markup being applied at all. This is a plausible contributor to "the app's number doesn't match what I expected" complaints, independent of anything in the quote-history reporting effort.

**Suggested next step:** decide on one canonical markup calculation, verify it against all three CWT/non-CWT and per-package/per-shipment cases each path currently handles, and consolidate the three implementations into one.
