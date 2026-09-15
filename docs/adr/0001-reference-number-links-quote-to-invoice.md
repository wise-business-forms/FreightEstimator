# Link Quotes to Invoices with a new Reference Number field, not heuristic matching

We want Rate Variance reporting to compare a Quote's Calculated Rate against its shipment's eventual Invoice/Actual Cost. No field captured today (address, weight, date, requester) is a reliable shared key between the two — `Invoice.AirBillProNumber` (the one candidate) is hardcoded to `"-"` in the current Margin Report query, and no order/PO number is captured at quote time at all.

We considered fuzzy-matching historical Quotes to Invoices by destination + weight + date proximity, and reporting only lane-level aggregates instead of per-shipment comparisons. We rejected both: heuristic matching produces probabilistic, sometimes-wrong pairings on busy lanes, and aggregate-only reporting can't tell a requester "this specific quote was off."

Instead, the Quote flow will capture a new **Reference Number** field going forward, which must also appear on the shipment's ERP invoice so the two can be joined exactly. This is deliberately not backfillable: Quotes made before this field exists can never be matched to an Invoice. Rate Variance reporting will only ever cover shipments quoted after this change ships.
