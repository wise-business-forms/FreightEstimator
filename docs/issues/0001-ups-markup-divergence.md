# Calculated Rate is computed differently depending on which code path produced it

**Found while:** designing quote-history persistence (see `docs/adr/0002-app-generated-quote-id.md`).
**Status:** in progress — shadow-mode validation underway (see `docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md`). Will close once all four call sites below are live on the new `CalculatedRate.Apply` module, not just the first one migrated.

Four independent code paths each compute a service's final marked-up price, and nothing guarantees they agree for the same shipment/service:

1. **GRID 1, single-plant mode** (`ShipmentConfirmation`, single-location branch): markup applied inline in `ApplyPlantCharges` (`PlantController.cs:507-583`). For the CWT/negotiated case: `CustomerRate = NegotiatedRateCharges_TotalCharge + (NegotiatedRateCharges_TotalCharge * Plant_Surcharge / 100)`, then per-package/per-shipment charges added (`PlantController.cs:572-575`). Non-CWT shipments get no markup at all.
2. **GRID 1, multi-plant ("rate from multiple locations") mode**: `CustomerRate` is **never marked up at all** — the multi-plant loop (`PlantController.cs:214-291`) computes CWT eligibility and plant charge lookups but never applies them to `CustomerRate`. An attempt to call `RateCalculations.CalculateUPSRate` here is commented out (`PlantController.cs:217`). This path silently shows the raw carrier rate as if it were the final price.
3. **GRID 2 (ground freight only)**: calls the separate static `RateCalculations.CalculateUPSRate` (`Models/Services/RateCalculations.cs:201`), a third, differently-structured markup implementation, invoked only from `PlantController.cs:435`. Its CWT branch is dead code (`bool cwt = false` is never reassigned), and markup/per-package/per-shipment charges are only ever populated for `serviceName == "UPSGroundFreight"` — every other service name returns the raw carrier rate.
4. **LTL** (`GetLessThanTruckloadRates_TI`, `PlantController.cs:1052-1407`): a fourth, distinct formula (`PlantController.cs:1347-1361`), shared by LTL's single- and multi-plant modes but unrelated to any of the three UPS formulas above.

Four formulas for what's conceptually the same operation ("apply our markup to a carrier rate") means the same quote could show a different final price depending on which code path produced it — including, in the UPS multi-plant case, no markup being applied at all. This is a plausible contributor to "the app's number doesn't match what I expected" complaints, independent of anything in the quote-history reporting effort.

**Next step:** consolidate all four into one `CalculatedRate.Apply(CarrierRate, ChargeSchedule, RatingContext)` module, rolled out one call site at a time in shadow mode so the live quote calculation isn't changed until each migration is verified against real traffic. See `docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md` for the full decision.
