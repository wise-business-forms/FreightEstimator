# Residential delivery surcharge was never quoted, only invoiced

**Found while:** investigating a specific Butler → Annapolis, MD shipment (`1Z1893120396614433`, shipped 2026-08-26) where UPS invoiced $175.94 against a $143.36 quote.
**Status:** Fixed — see `Models/Services/UPSRequest.cs`'s `RateRequest`.

The Rate request UPS's Rating API receives for every quote never told UPS to evaluate whether the delivery address is residential. UPS's Address Validation (XAV) response — which this app already calls before every quote, and already parses into `Shipment.Address_Classification` (`Models/Services/UPSRequest.cs`, the address-validation step of the `UPSRequest` constructor) — carries exactly that answer (`Business` / `Residential` / `Unknown`). That field was only ever used to display a label on the confirmation screen (`Views/Plant/ShipmentConfirmation.cshtml`, "Address is: ..."); it was never passed back into the Rate request that follows it.

The practical effect: any delivery UPS classifies as residential is quoted with no residential surcharge, and that surcharge (commonly $50–$100+) shows up for the first time on the invoice, with nothing on the quote to have warned the customer.

For the shipment that surfaced this: UPS's own address database returned `Unknown` for the destination at quote time — it only determined the address was residential at pickup. So this fix closes the gap for addresses UPS already confidently classifies in advance; it does not, and structurally cannot, close the gap for addresses UPS itself can't classify at quote time. That remainder is a business/quoting-policy question (e.g. a disclaimer, or a contingency buffer for `Unknown`-classified addresses), not a code fix, and is not addressed here.

**Fix:** `UPSRequest.RateRequest` now adds `"ResidentialAddressIndicator": ""` to the `ShipTo.Address` block of the Rate request whenever `Shipment.Address_Classification` is `"Residential"`. Covered by `Tests/ResidentialAddressIndicatorTests.cs` (residential includes it; unknown, commercial, and null all omit it).
