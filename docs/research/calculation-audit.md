# Calculation audit: every number FreightEstimator produces

**Scope:** a read-only audit of every calculation that produces or affects a number: Carrier Rate parsing, Calculated Rate (markup, per-package and per-shipment Plant Charges), CWT eligibility, weight and dimension handling, surcharges (fuel, residential, accessorial), multi-plant/GRID/LTL logic, shadow-mode comparison math, persistence to SQL, displayed values (Razor and JS), and report aggregates.
**Snapshot:** branch `MarginReport` at `49cbe3f`, plus the uncommitted working-tree changes present at audit time (`Controllers/ReportsController.cs`, `Views/Reports/MarginReport.cshtml` and others). Line numbers refer to that working tree.
**Method:** static reading only. Nothing was executed. Findings marked *(static)* describe a runtime failure that follows from the code as written and should be reproduced before anyone acts on it.
**Links:** paths start with `/` and resolve from the repo root, e.g. [/Controllers/PlantController.cs#L164](/Controllers/PlantController.cs#L164).

Vocabulary follows [CONTEXT.md](/CONTEXT.md): **Quote**, **Carrier Rate**, **Calculated Rate**, **Plant Charges**, **Logistics Partner** (LTL; "TI" in code), **Quote History**, **Invoice / Actual Cost**, **Margin Report**. The four Calculated Rate call sites use the shadow-log names from [ADR 0003](/docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md): `GRID1Single`, `GRID1Multi`, `GRID2`, `LTL`.

---

## 1. Summary: top findings

| # | Sev | Finding |
|---|-----|---------|
| F1 | **High** | **ADR 0003's canonical formula contradicts a documented business rule.** The canonical formula marks up *every* Carrier Rate. But the code comment at [PlantController.cs:574-578](/Controllers/PlantController.cs#L574) records a 3/21/2025 GM decision: non-CWT uses the published rate, CWT uses the negotiated rate. The rate chart page [Views/Rates/Index.cshtml:47-194](/Views/Rates/Index.cshtml#L47) says non-CWT gets "Mark Up Calculation: None" and "Per Ctn / Per Shipment: None". ADR 0003 and `docs/issues/0001` call non-CWT's missing markup a bug, while these two sources call it policy. Every `GRID1Single` non-CWT shadow delta will equal `published × markup%`, and the business has to decide whether that delta is "the known bug" or intended behavior. |
| F2 | **High** | **The multi-plant grid would put rates under the wrong service names.** `MultiView` fills one flat list of rates per plant. Each list holds every service UPS returned, sorted into `UPSServiceCodeOrder`. It then walks that same order, skips services missing from a 6-item `supportedServices` list, and reads the lists with a counter that only advances on supported services ([PlantController.cs:1676-1714](/Controllers/PlantController.cs#L1676)). `UPS2ndDayAir` is 3rd in the order ([Configuration.cs:142](/Models/Configuration.cs#L142)) but not in `supportedServices`. So when UPS returns 2nd Day Air, the rows from `SecondDayAirAM` down each show the previous service's rate. |
| F3 | **High** | **The multi-plant mode ("Rate from Multiple Locations = Yes") looks unable to finish a request *(static)*.** (a) It never sets `shipment.shopCompareRates`, but the view reads `Model.shopCompareRates[0]` unconditionally ([ShipmentConfirmation.cshtml:190](/Views/Plant/ShipmentConfirmation.cshtml#L190)), which throws a NullReferenceException. The view never renders `ViewBag.MultiView` at all ([ShipmentConfirmation.cshtml:5](/Views/Plant/ShipmentConfirmation.cshtml#L5) assigns it and nothing uses it). (b) A CWT-eligible service reads `rateCalculations.PerPackageChargeCWT[...]` ([PlantController.cs:277](/Controllers/PlantController.cs#L277)), but the default constructor only fills the non-CWT dictionaries ([RateCalculations.cs:68](/Models/Services/RateCalculations.cs#L68), [108-126](/Models/Services/RateCalculations.cs#L108)), so this throws KeyNotFoundException. (c) Adding Ground Freight calls `dataTable.Columns.Remove("CWT")` on a table that has no "CWT" column ([PlantController.cs:308-324](/Controllers/PlantController.cs#L308)), which throws ArgumentException. (d) `shipmentResponse = shipment` is a reference copy ([PlantController.cs:209](/Controllers/PlantController.cs#L209)), so after the loop `shipment.PlantId` holds the *last* plant. GRID2, LTL and `ViewBag.PlantRates` then quote from that plant, not the one the user picked. Shadow rows for `GRID1Multi` may therefore come from requests that errored part-way through. |
| F4 | **High** | **Fractional package weights crash the GRID2 (Ground Freight) and GRID1Multi paths *(static)*.** `CalculateUPSRate` and `HundredWeightEligable` run `Convert.ToInt16(string)` on `float.ToString()` weights ([RateCalculations.cs:216](/Models/Services/RateCalculations.cs#L216), [220](/Models/Services/RateCalculations.cs#L220), [326](/Models/Services/RateCalculations.cs#L326)). A weight like `"12.5"` is not an integer string and throws FormatException. Anything over 32,767 throws OverflowException. In `CalculateUPSRate` the weight it computes is never even used (see F16). |
| F5 | **High** | **The GRID2 shadow comparison uses the wrong carrier's per-package and per-shipment charges.** For `UPSGroundFreight`, `RateCalculationsChargeSchedule` takes the markup from the `GF` carrier but the per-package and per-shipment charges from the `UPS` carrier ([RateCalculationsChargeSchedule.cs:34-38](/Models/Services/RateCalculationsChargeSchedule.cs#L34), [68-69](/Models/Services/RateCalculationsChargeSchedule.cs#L68)). The live GRID2 formula uses `GF` for all three ([RateCalculations.cs:242-245](/Models/Services/RateCalculations.cs#L242)). Beyond that, the live GRID2 formula already *is* the canonical formula ([RateCalculations.cs:293-296](/Models/Services/RateCalculations.cs#L293)). So GRID2 deltas come only from this lookup mismatch, not from "the known bug". ADR 0003's gate ("every divergence is explained by the known bug") cannot pass for GRID2 as the code stands. |
| F6 | **Medium** | **Legacy paths and the shadow path read Plant Charges from different sources.** `GRID1Single` reads table `PlantCarrierCharges2017` with no account filter ([Plant.cs:101](/Models/Plant.cs#L101)). The shadow module, GRID1Multi and GRID2 read stored procedure `GetPlantCharges` with `@AcctNumber = 0` ([RateCalculations.cs:83-86](/Models/Services/RateCalculations.cs#L83), [RateCalculationsChargeSchedule.cs:25](/Models/Services/RateCalculationsChargeSchedule.cs#L25), [34](/Models/Services/RateCalculationsChargeSchedule.cs#L34)). Live LTL passes the real `shipment.AcctNum` ([PlantController.cs:1113](/Controllers/PlantController.cs#L1113)). If the two sources disagree, or if the proc applies account-specific overrides, shadow deltas will include data differences that have nothing to do with the formula. |
| F7 | **Medium** | **`QuoteServiceCalculation` records the wrong Carrier Rate for GRID1Single and GRID2.** `FromUps` decides negotiated vs published with `bool.TryParse(service.CWT)` ([QuoteServiceCalculation.cs:34](/Models/QuoteServiceCalculation.cs#L34)). On these paths CWT is `"Yes"`/`"No"` ([UPSRequest.cs:335](/Models/Services/UPSRequest.cs#L335), [342](/Models/Services/UPSRequest.cs#L342)), which does not parse. So `IsNegotiatedRate` is NULL and `CarrierRate` is always the published figure, even when the negotiated rate was the base. For GRID2 it is worse: "published" has been overwritten with the FRS **GrossCharge** ([UPSRequest.cs:382](/Models/Services/UPSRequest.cs#L382)). In the sample payload that is $852.64 against a real base of $146.93 ([documentation/response.json:44-76](/documentation/response.json#L44)). GRID2 also stores the **markup percent in `Plant_ShipmentCharge`** ([PlantController.cs:450](/Controllers/PlantController.cs#L450)), so `PerShipmentCharge` holds a percentage. |
| F8 | **Medium** | **CWT eligibility is computed five different ways**, with different weight formulas, service sets and package minimums (see §4.2). The one that feeds GRID1Multi double-counts a package. The one that feeds GRID1Single hard-codes `>= 2` packages and leaves out `UPSSaver`. `NextDayAirEarlyAM` counts as CWT-eligible on the live path, although two other places label it "AIR-NN: negotiated not allowed for CWT". |
| F9 | **Medium** | **UPS Ground Freight ignores the freight class the user picks.** UPS GF always sends `FreightClass: "65"` ([UPSRequest.cs:648](/Models/Services/UPSRequest.cs#L648), [694](/Models/Services/UPSRequest.cs#L694)). LTL sends the user's selection ([PlantController.cs:1157](/Controllers/PlantController.cs#L1157), [1256](/Controllers/PlantController.cs#L1256)), and the confirmation page shows that selection for both ([ShipmentConfirmation.cshtml:56-61](/Views/Plant/ShipmentConfirmation.cshtml#L56)). |
| F10 | **Medium** | **Every package is rated as a 5×5×5 in box**, for UPS ([UPSRequest.cs:670-678](/Models/Services/UPSRequest.cs#L670)) and LTL ([PlantController.cs:1261](/Controllers/PlantController.cs#L1261)). Dimensional weight therefore never applies to a Quote, and the app ignores UPS's returned `BillingWeight` ([documentation/response.json:29-35](/documentation/response.json#L29)), computing its own billing weight from actual weight only. The Invoice will be based on real dimensions. |

The remaining findings (F11 to F30) are in §5.

---

## 2. Units and numeric conventions (applies throughout)

| Quantity | Unit | Evidence |
|---|---|---|
| Package weight | **lb**, `float` | `Shipment.package_weight` / `last_package_weight` are `float` ([Shipment.cs:26-27](/Models/Shipment.cs#L26)). UPS request sends `"Code": "LBS"` ([UPSRequest.cs:682](/Models/Services/UPSRequest.cs#L682)). LTL sends `units="lb"` ([PlantController.cs:1258](/Controllers/PlantController.cs#L1258)). UPS returns `LBS` ([response.json:29-34](/documentation/response.json#L29)). |
| Package dimensions | **in**, hard-coded 5×5×5 | [UPSRequest.cs:672-677](/Models/Services/UPSRequest.cs#L672), [PlantController.cs:1261](/Controllers/PlantController.cs#L1261) |
| Package count | `int` | [Shipment.cs:25](/Models/Shipment.cs#L25) |
| Money from UPS | **USD dollars**, decimal strings with 2 places (e.g. `"146.93"`) | [response.json:36-76](/documentation/response.json#L36) |
| Money from the Logistics Partner | dollars as strings (`SubTotal`, `Total`, `Charge/Amount`) | [PlantController.cs:1363-1379](/Controllers/PlantController.cs#L1363) |
| Money in memory (legacy) | `double` (`UPSService.CustomerRate`) plus **strings** for every component (`Plant_Surcharge`, `Plant_PerPackageCharge`, `RatedShipment_*`) | [UPSService.cs:17](/Models/UPSService.cs#L17), [64-101](/Models/UPSService.cs#L64) |
| Plant Charges (legacy) | `double` | [PlantCharges.cs:12-21](/Models/PlantCharges.cs#L12) |
| Money in the new module | `decimal` | [CalculatedRate.cs:10-14](/Models/Services/CalculatedRate.cs#L10), [PlantServiceCharges.cs:9-11](/Models/Services/PlantServiceCharges.cs#L9) |
| Markup | **Percent form (10 = 10%)** everywhere. Every consumer divides by 100. | [PlantController.cs:588](/Controllers/PlantController.cs#L588), [RateCalculations.cs:296](/Models/Services/RateCalculations.cs#L296), [PlantController.cs:1126](/Controllers/PlantController.cs#L1126), [CalculatedRate.cs:34](/Models/Services/CalculatedRate.cs#L34), display `@_plantCwtSurcharge%` [ShipmentConfirmation.cshtml:235](/Views/Plant/ShipmentConfirmation.cshtml#L235). LTL keeps it internally as a fraction (`plantSurcharge = x/100`) and multiplies it back by 100 when logging ([QuoteServiceCalculation.cs:95](/Models/QuoteServiceCalculation.cs#L95)). |
| Persisted money | SQL `DECIMAL(18,2)`; markup `DECIMAL(9,4)` | [001_create_quote_service_calculation.sql:23-28](/Database/001_create_quote_service_calculation.sql#L23), [002_create_calculated_rate_shadow_log.sql:19-21](/Database/002_create_calculated_rate_shadow_log.sql#L19), parameters [Log.cs:342-347](/Models/Services/Log.cs#L342), [CalculatedRateShadowLog.cs:36-37](/Models/Services/CalculatedRateShadowLog.cs#L36) |

**Rounding: the app never explicitly rounds money.** There is no `Math.Round`, `MidpointRounding`, `F2`/`N2` format or `Math.Ceiling` on money anywhere in `.cs`/`.cshtml`. The only `Math.Ceiling` is report page count ([ReportsController.cs:154](/Controllers/ReportsController.cs#L154)). Rounding to cents happens in only two places:
1. **Display:** `ToString("C")` (current culture), e.g. [ShipmentConfirmation.cshtml:212](/Views/Plant/ShipmentConfirmation.cshtml#L212), [PlantController.cs:1405](/Controllers/PlantController.cs#L1405). The value underneath is not rounded.
2. **Persistence:** SQL parameters declared `Precision=18, Scale=2` ([Log.cs:342-347](/Models/Services/Log.cs#L342), [CalculatedRateShadowLog.cs:36-37](/Models/Services/CalculatedRateShadowLog.cs#L36)). Whether a value with more than 2 decimal places is *rounded* or *truncated* here depends on the SqlClient version. See technical question T1.

Because nothing rounds *before* summing, the Calculated Rate is an unrounded `double`. Each component (markup, per-package total) is not rounded to cents before it is added.

**Parsing is culture-sensitive everywhere.** `Double.Parse`, `float.Parse`, `decimal.TryParse`, `Convert.ToDouble` and `ToString()` run with no `CultureInfo`, e.g. [PlantController.cs:582-608](/Controllers/PlantController.cs#L582) and [Plant.cs:110-119](/Models/Plant.cs#L110). This only matters if the server's culture ever stops being en-US. Low.

---

## 3. Request flow (where each number comes from)

`PlantController.ShipmentConfirmation` ([PlantController.cs:149-520](/Controllers/PlantController.cs#L149)):

1. Billing weight is computed (§4.1).
2. **GRID1** (skipped when `number_of_packages > 50`, [L197](/Controllers/PlantController.cs#L197)): UPS "Shop" rate call, then either **GRID1Multi** ([L199-326](/Controllers/PlantController.cs#L199)) or **GRID1Single** ([L328-438](/Controllers/PlantController.cs#L328)).
3. **GRID2** Ground Freight if selected ([L442-472](/Controllers/PlantController.cs#L442)).
4. **GRID3 / LTL** if selected ([L475-504](/Controllers/PlantController.cs#L475)), via `GetLessThanTruckloadRates_TI` ([L1089-1515](/Controllers/PlantController.cs#L1089)).
5. View: [ShipmentConfirmation.cshtml](/Views/Plant/ShipmentConfirmation.cshtml).

Every `new UPSRequest(...)` makes two HTTP calls: Address Validation (XAV), then Rate. It also **mutates the shared `Shipment`** (address, zip suffix, `ErrorMessage`, `Address_Classification`, and, if the package count is 0, packages and weight) ([UPSRequest.cs:38-179](/Models/Services/UPSRequest.cs#L38)).

---

## 4. Inventory of calculations

### 4.1 Weight, package, and request-shape handling

| # | Location | Formula / behavior | Types / rounding | Edge cases |
|---|---|---|---|---|
| W1 | [PlantController.cs:162-168](/Controllers/PlantController.cs#L162) | **Billing weight.** If `last_package_weight > 0`: `package_weight × (n − 1) + last_package_weight`. Otherwise `n × package_weight`. Shown as "Weight: X lbs" ([ShipmentConfirmation.cshtml:106](/Views/Plant/ShipmentConfirmation.cshtml#L106)) and used by the live CWT flag (C1). | `float`, not rounded. | `n = 0` gives 0 (but see W3). A negative weight goes through unchecked: there is no validation, and the input is `type="number"` ([HtmlHelpers.cs:22-25](/Controllers/HtmlHelpers.cs#L22)). An empty input turns into `0` on blur ([HtmlHelpers.cs:25](/Controllers/HtmlHelpers.cs#L25)), so "0" also means "no distinct last package". Actual weight only: no dimensional weight, and UPS's own `BillingWeight` is ignored. |
| W2 | [PlantController.cs:179](/Controllers/PlantController.cs#L179) | `rateResponse.Weight = (int)shipment.package_weight` | Truncates (does not round) the weight of *one package*, not the total. | Harmless: the only consumer, `Log.LogRequest_Rate(RateResponse)`, is fully commented out ([Log.cs:14-262](/Models/Services/Log.cs#L14)). |
| W3 | [UPSRequest.cs:46-50](/Models/Services/UPSRequest.cs#L46) | If `number_of_packages == 0`, **silently set to 1 package of 1 lb**. | Mutates the Shipment. | The Quote becomes a 1 lb quote with no warning, while the page still shows billing weight 0 (W1 has already run). |
| W4 | [UPSRequest.cs:618-639](/Models/Services/UPSRequest.cs#L618), [660-699](/Models/Services/UPSRequest.cs#L660) | UPS packages: with a last package, `n − 1` packages at `package_weight` plus one at `last_package_weight`; otherwise `n` at `package_weight`. `NumOfPieces = n` ([L609](/Models/Services/UPSRequest.cs#L609)). Every package is 5×5×5 IN, `PackagingType 02`. `MinimumBillableWeightIndicator: "X"` ([L653](/Models/Services/UPSRequest.cs#L653)). | Weight is written as `float.ToString()`. | Matches W1. Dimensions are always 5×5×5 (F10). |
| W5 | [UPSRequest.cs:643-651](/Models/Services/UPSRequest.cs#L643), [689-695](/Models/Services/UPSRequest.cs#L689) | Ground Freight: `FreightClass` is always `"65"`, ignoring `freight_class_selected`. | — | F9. |
| W6 | [UPSRequest.cs:565-568](/Models/Services/UPSRequest.cs#L565) | Residential: adds `ResidentialAddressIndicator` when `Address_Classification == "Residential"` (case-insensitive), per `docs/issues/0002`. UPS then includes the surcharge in `TotalCharges`. | — | `Unknown`/empty classification means no indicator (known gap, see CONTEXT.md). Note the surcharge then **becomes part of the Carrier Rate and gets marked up** on paths that apply markup (Q3). |
| W7 | [PlantController.cs:1249-1289](/Controllers/PlantController.cs#L1249) | LTL line items: one `<Item>` per package (`n − 1` + last, or `n`). `units="lb"`, 5×5×5 in. Freight class is the user's selection (default 65, [L97](/Controllers/PlantController.cs#L97)). | `float.ToString()` | Matches W1. Every item is `sequence="1"`. |
| W8 | [PlantController.cs:1294-1299](/Controllers/PlantController.cs#L1294) | LTL Drop city is `shipment.Corrected_City`. | — | `Corrected_City` is only set when UPS changed the address ([UPSRequest.cs:122-132](/Models/Services/UPSRequest.cs#L122)). Otherwise it is null, so an empty `<City>` is sent. Zip and state are still sent. |
| W9 | [PlantController.cs:1831-1855](/Controllers/PlantController.cs#L1831) | LTL accessorial flags. | — | Pickup options map to *delivery* codes: residential pickup becomes `RESD`, limited-access pickup becomes `LAPD` (delivery), inside pickup becomes `IDEL`. Liftgate pickup and delivery both send `LIFT`, so the code can be sent twice. `STOPOFF` is marked "? Not sure". Any of these can change the quoted accessorial charges (F18). |
| W10 | [Quotes.cshtml:148-164](/Views/Reports/Quotes.cshtml#L148) | "Re-run" link rebuilds weights from logged JSON: first package weight, plus last weight if it differs. `includeLtl = ParsedServices.Count > 1`. | `int.TryParse` for package count (0 on failure). | The LTL flag is a heuristic: any UPS Shop response with more than one service sets it (F27). |

### 4.2 CWT (hundredweight) eligibility: five implementations

The **live** threshold constants: air ≥ 100 lb and ≥ 2 packages, ground ≥ 200 lb and ≥ 2 packages ([Configuration.cs:82-86](/Models/Configuration.cs#L82)). These are `double`.

| # | Location | Used by | Total-weight formula | Services | Min packages | Notes |
|---|---|---|---|---|---|---|
| C1 | [UPSRequest.cs:338-350](/Models/Services/UPSRequest.cs#L338) (`ParseUPSService`) | **GRID1Single (live)**, GRID2 display | `shipment.billing_weight` (W1, correct) | Air: 01, 02, 13, 59, **14**. Ground: 03, 12 (**not 65**) | Hard-coded `>= 2` | Sets `CWT = "Yes"`/`"No"`. The comment says air CWT is "≥100 but <200 lbs", but there is no upper bound in code. |
| C2 | [RateCalculations.cs:317-361](/Models/Services/RateCalculations.cs#L317) (`HundredWeightEligable`) | **GRID1Multi**, and so the `isCwtEligible` passed to its shadow rows | `Int16(pw) × n + Int16(last)`, where an empty `last` is replaced by `pw` | Air: 14, 13, 02, 59, 01. Ground: 03, 12, 65 | `Configuration` | **Over-counts**: it adds `last` on top of `n` full packages. With no distinct last package it adds `+0` (`"0"` is not empty), which is correct by accident. **Throws** on fractional weights (F4). Sets `CWT = "True"`/`"False"`. This is the "billing-weight over-count bug" ADR 0003 deliberately left out of scope. |
| C3 | [RateCalculations.cs:212-221](/Models/Services/RateCalculations.cs#L212), [249-250](/Models/Services/RateCalculations.cs#L249) (`CalculateUPSRate`) | GRID2 (result not used) | `n > 1`: `Int16(pw) × (n − 1) + Int16(last)`, which **under-counts** by one package when `last = 0`. `n ≤ 1`: `Int16(pw)` | — | `Configuration` | `isAirCWT`/`isGroundCWT` are computed and then ignored. `cwt` is hard-coded `false` ([L211](/Models/Services/RateCalculations.cs#L211)). The `Int16` parse still throws (F4). |
| C4 | [PlantController.cs:661-714](/Controllers/PlantController.cs#L661) (`ValidateStateForCWT`) | **Never called** | `(n − 1) × pw + last` or `n × pw` | air OR ground | `Configuration` | Dead code. Its result `StateIsValid` is discarded. |
| C5 | [UPSService.cs:38-58](/Models/UPSService.cs#L38) (`CWTCodes`) and [RateCalculations.cs:254-263](/Models/Services/RateCalculations.cs#L254) (`dServiceTypes`) | Not used in any decision | — | 14 is `"AIR-NN"` ("no negotiated rate… not allowed") | — | Conflicts with C1, which *does* treat 14 as CWT and prices it on the negotiated rate (Q5). |

The rate chart page says "Air > 100lbs; Ground > 200lbs" ([Rates/Index.cshtml:225](/Views/Rates/Index.cshtml#L225)) but also "< 100 / < 200" for non-CWT ([L75](/Views/Rates/Index.cshtml#L75)). The code uses `>=`, so exactly 100 lb (air) or 200 lb (ground) is CWT. That is consistent with "<" for non-CWT but not with ">" for CWT. Low.

### 4.3 Carrier Rate parsing

| # | Location | What becomes the Carrier Rate | Edge cases |
|---|---|---|---|
| P1 | [UPSRequest.cs:333](/Models/Services/UPSRequest.cs#L333) | `CustomerRate = Double.Parse(TotalCharges.MonetaryValue ?? "0.0")`, the UPS **published** total (includes fuel, residential and other surcharges). | A missing value becomes 0.0. |
| P2 | [UPSRequest.cs:352-356](/Models/Services/UPSRequest.cs#L352) | Strings kept: `TransportationCharges`, `BaseServiceCharge`, `ServiceOptionsCharges`, published `TotalCharges`, and `NegotiatedRateCharges.TotalCharge`. | Every missing value defaults to `"0"`. **A missing negotiated rate therefore gives a Carrier Rate of $0** for CWT services in GRID1Single ([PlantController.cs:581-583](/Controllers/PlantController.cs#L581)). The Calculated Rate then collapses to just the fixed Plant Charges, silently (F20). |
| P3 | [UPSRequest.cs:359-383](/Models/Services/UPSRequest.cs#L359) | **Ground Freight** (code 03 with `FRSShipmentData`): `Accessorial = Negotiated − BaseServiceCharge` (double). `Published` is **overwritten with `FRSShipmentData.TransportationCharges.GrossCharge`**. `CustomerRate` stays `TotalCharges`, which for FRS equals the negotiated net (sample: TotalCharges 146.93 = Negotiated 146.93, GrossCharge 852.64, DiscountPercentage 85.0, [response.json:44-76](/documentation/response.json#L44)). | The itemized-charges loop reads `service["RatedResponse"]["RatedShipment"]["RatedPackage"]`, a path that does not exist under a `RatedShipment` node, and its totals are never used ([L362-376](/Models/Services/UPSRequest.cs#L362)). Dead. |
| P4 | [UPSRequest.cs:407-447](/Models/Services/UPSRequest.cs#L407) | `UPSServices` getter: parses the array, sorts by `UPSServiceCodeOrder`. | If `ErrorMessage` is neither `""` nor `"Address not validated"`, or the parse throws, it returns **null**. `GetCompareRates` then calls `.ToList()` on null ([PlantController.cs:729](/Controllers/PlantController.cs#L729)), and GRID2 indexes `UPSServices[0]` ([PlantController.cs:445](/Controllers/PlantController.cs#L445)). A UPS error response therefore gives an exception page instead of the error message the view is built to show ([ShipmentConfirmation.cshtml:455-466](/Views/Plant/ShipmentConfirmation.cshtml#L455)) (F19) *(static)*. A single-object `RatedShipment` returns early without sorting ([L433-438](/Models/Services/UPSRequest.cs#L433)). |
| P5 | [PlantController.cs:1363-1382](/Controllers/PlantController.cs#L1363) | LTL price sheets: `Rate = SubTotal`, `TotalCost = Total`, `FuelCharge` = first `ACCESSORIAL_FUEL` amount, `TotalAccessorialAmount` = decimal sum of all `ACCESSORIAL` amounts (a failed parse counts as 0). Carrier Rate is `double.Parse(Total)` ([L1404](/Controllers/PlantController.cs#L1404)). | `double.Parse(priceSheet.FuelCharge)` ([L1413](/Controllers/PlantController.cs#L1413)) **throws if a carrier has no fuel line**, and the outer `catch → throw` ([L1510-1513](/Controllers/PlantController.cs#L1510)) takes down the whole LTL grid. A null `Total` gives `Convert.ToDouble(null) = 0` at L1386 but throws at L1404. A `WebException` is swallowed to the console ([L1496-1509](/Controllers/PlantController.cs#L1496)) and returns `UPSServices = null`, which then throws at [L484](/Controllers/PlantController.cs#L484) (F21). |

### 4.4 Calculated Rate: the four live call sites and the canonical module

**Order of operations, all paths:** the markup percentage (when applied) multiplies the carrier's **all-in total**: UPS `TotalCharges` (published or negotiated), or the Logistics Partner's `Total`, which already includes fuel, accessorials and (for UPS since 0002) residential. Per-package and per-shipment charges are then **added after** the markup and are **never marked up**. No path adds anything *after* the fixed charges. No path marks up only the base (line-haul) charge.

#### 4.4.1 GRID1Single: `ApplyPlantCharges` ([PlantController.cs:530-622](/Controllers/PlantController.cs#L530))

```
CarrierId      = (CWT == "YES") ? "UPSCWT" : "UPS"                          L543
PerPkg, PerShp = PlantCarrierCharges2017 row for CarrierId                  L545-546
Markup%        = that row's column for the service                          L549-572
isCWT          = CWT in {"TRUE","YES"}                                      L580
Base           = isCWT ? Negotiated.TotalCharge : Published.TotalCharges    L581-583
CalculatedRate = isCWT ? Base + Base × Markup%/100 : Base                   L585-596
CalculatedRate += PerShp + PerPkg × n                                       L606-608
```
- Types: `double` throughout. Charges round-trip as `double → string → double.Parse`.
- The comment at L574-578 records the 3/21/2025 GM decision (F1). **Non-CWT gets no markup but does get the per-package and per-shipment charges**, while the rate chart says per-carton and per-shipment are "None" for non-CWT ([Rates/Index.cshtml:146-194](/Views/Rates/Index.cshtml#L146)) (Q2).
- `RatedShipment_Surcharge = Markup%/100 × Negotiated` is computed even for non-CWT, where no markup was applied ([L599-602](/Controllers/PlantController.cs#L599)). It is not displayed for UPS. Low.
- Only 7 services are handled ([L365-431](/Controllers/PlantController.cs#L365)). Any other service UPS returns (e.g. `UPSSaver`, `UPSStandard`, the Worldwide services) keeps the raw published `CustomerRate` and has `Plant_PerPackageCharge = null`. The view then throws at `float.Parse(null)` ([ShipmentConfirmation.cshtml:197](/Views/Plant/ShipmentConfirmation.cshtml#L197)) and at `serviceNameTranslations[...]` ([L210](/Views/Plant/ShipmentConfirmation.cshtml#L210)) (F22) *(static)*.
- A plant with no `UPS` or `UPSCWT` row gives `FirstOrDefault(...)` null, so NullReferenceException ([L545](/Controllers/PlantController.cs#L545)).
- Guarded by `ErrorMessage` being `""` or `"Address not validated"` ([L354](/Controllers/PlantController.cs#L354)).

#### 4.4.2 GRID1Multi ([PlantController.cs:199-326](/Controllers/PlantController.cs#L199))

```
Carrier Rate   = TotalCharges (published)
CWT            = HundredWeight*Eligable(...)  (C2, over-counting)          L220-273
PerPkg, PerShp = looked up but NEVER applied                                L275-284
CalculatedRate = Carrier Rate  (markup call commented out, L217)
```
- See F3 for the crash paths and the `PlantId` aliasing. The shadow `existingCalculatedRate = CustomerRate` ([L296](/Controllers/PlantController.cs#L296)) is the raw published rate.
- Multi-plant Ground Freight rows are also raw, with no `CalculateUPSRate` ([L313-323](/Controllers/PlantController.cs#L313)).
- `MultiView` rate alignment bug: F2. The table has fixed ALP/BUT/FTW/POR columns ([L1664-1669](/Controllers/PlantController.cs#L1664)). Any other active plant's rates are dropped, and a missing plant gives ArgumentOutOfRangeException. The `cwt` column is the literal `"TBD"`.

#### 4.4.3 GRID2 Ground Freight: `CalculateUPSRate` ([RateCalculations.cs:201-307](/Models/Services/RateCalculations.cs#L201)), called at [PlantController.cs:446](/Controllers/PlantController.cs#L446)

```
PerPkg, PerShp, Markup% = GetPlantCharges('GF', acct 0)                     L242-245
cwt = false  (hard-coded; CWT branch L272-287 is dead)
serviceName == "UPSGroundFreight":
  total = PerShp + PerPkg × n + (Markup%/100 × rate) + rate                 L293-296
```
- This **is** the canonical formula. The `default:` branch ([L298-302](/Models/Services/RateCalculations.cs#L298)) leaves out per-shipment, and the `NextDayAirEarlyAM` branch returns only the fixed charges with no carrier rate ([L292-296](/Models/Services/RateCalculations.cs#L292)). Both are unreachable because the only caller passes `"UPSGroundFreight"`.
- The dead CWT branch uses `0.7 × currentRate` and labels it "negotiated divided by 70%" ([L274-275](/Models/Services/RateCalculations.cs#L274)). It *multiplies* by 0.7. The GM decision removed this adjustment ([PlantController.cs:578](/Controllers/PlantController.cs#L578)). Dead.
- `int.Parse(accountNumber)` ([L236](/Models/Services/RateCalculations.cs#L236)) throws on a non-numeric account, and its result is unused (charges always use acct 0).
- The fractional-weight crash is in this function (F4).
- After the call, `Plant_ShipmentCharge = UpchargeGround` (the **markup %**) ([PlantController.cs:448-450](/Controllers/PlantController.cs#L448)) (F7).
- Carrier Rate = `TotalCharges` = FRS net (P3). That is correct as a base, but the view and the log call GrossCharge "Published".

#### 4.4.4 LTL / Logistics Partner ([PlantController.cs:1089-1437](/Controllers/PlantController.cs#L1089))

```
PerPkg, PerShp, Up% = GetPlantCharges('TI', shipment.AcctNum) for shipment.PlantId   L1109-1137
markupFraction      = Up% > 0 ? Up%/100 : 0          (non-positive values ignored)
PerPkg              = PerPkg > 0 ? PerPkg : 0
PerShp              = PerShp > 0 ? PerShp : 0
CalculatedRate      = Total + markupFraction × Total + PerPkg × n + PerShp        L1386-1398
Display             = CalculatedRate.ToString("C")   (the only form shown)         L1405, view L415
```
- LTL markup is read from the `Ground` column of the `TI` carrier row ([L1121](/Controllers/PlantController.cs#L1121)).
- Charges are looked up with `shipment.PlantId`, not the loop's `plantCode` ([L1123-1137](/Controllers/PlantController.cs#L1123)). This is moot while `PlantId` can never be `"ALL"`: `Index` rejects any code not in `PlantLocations` ([L46](/Controllers/PlantController.cs#L46)).
- Sorting: `OrderBy(CustomerRate)` then `ThenBy(float.TryParse(published))` ([L1442-1446](/Controllers/PlantController.cs#L1442)). The comments say "TransitDays" and "TotalCharges", but it actually sorts by the raw carrier total. That is the same order as sorting by Calculated Rate, because the markup is monotonic. Low.
- `GetPlantUpcharges` ([L1797-1829](/Controllers/PlantController.cs#L1797)) returns `n × PerPkg + PerShp + Ground%`, **adding a percentage to dollars**. Its result (`PlantUpcharges`, [L1091](/Controllers/PlantController.cs#L1091)) is never used. Dead, but it costs an extra DB round-trip.

#### 4.4.5 Canonical module: `CalculatedRate.Apply` ([CalculatedRate.cs:29-39](/Models/Services/CalculatedRate.cs#L29))

```
Amount = CarrierRate + CarrierRate × (Markup% / 100m) + PerPkg × PackageCount + PerShp
```
- `decimal`, not rounded, no guards: negative markups and charges are applied as given (unlike LTL, which ignores them), and a negative or zero package count is accepted.
- Charges come from `RateCalculationsChargeSchedule` ([RateCalculationsChargeSchedule.cs:21-83](/Models/Services/RateCalculationsChargeSchedule.cs#L21)). Always `GetPlantCharges` with acct **0**. `UPS` vs `UPSCWT` chosen by `isCwtEligible`. `"LTL"` goes to `TI`. `"UPSGroundFreight"` takes markup from `GF` but **per-package and per-shipment from `UPS`/`UPSCWT`** (F5). Unknown services, missing keys and unparseable values all become **0** (`LookUp`, [L75-83](/Models/Services/RateCalculationsChargeSchedule.cs#L75)). A missing charge row is therefore indistinguishable from a real zero charge (F24).
- Tests cover the formula with in-memory charges ([Tests/CalculatedRateTests.cs](/Tests/CalculatedRateTests.cs)). There are no tests for the production adapter's carrier and column mapping, which is where F5 lives.

#### 4.4.6 Side-by-side

| | Base (Carrier Rate) | Markup % applied? | Per-pkg / per-ship applied? | Charge source (carrier row) | Account |
|---|---|---|---|---|---|
| GRID1Single CWT | UPS negotiated total | Yes | Yes | `PlantCarrierCharges2017`, `UPSCWT` | none |
| GRID1Single non-CWT | UPS published total | **No** | Yes | `PlantCarrierCharges2017`, `UPS` | none |
| GRID1Multi | UPS published total | **No** | **No** | `GetPlantCharges` `UPS` (lookup only) | 0 |
| GRID2 | UPS FRS `TotalCharges` (net) | Yes | Yes | `GetPlantCharges` `GF` | 0 |
| LTL | Logistics Partner `Total` | Yes if > 0 | Yes if > 0 | `GetPlantCharges` `TI` | `AcctNum` |
| **Canonical (shadow)** | as passed by caller | Always | Always | `GetPlantCharges` `UPS`/`UPSCWT`/`TI`; GF: markup `GF`, fixed `UPS` | 0 |

### 4.5 Shadow-mode comparison math

- Recorder: [ShadowComparisonRecorder.cs:16-53](/Models/Services/ShadowComparisonRecorder.cs#L16). Existing value comes in as `(decimal)double` and the new value as `decimal`. Both are sent as `DECIMAL(18,2)` ([CalculatedRateShadowLog.cs:36-37](/Models/Services/CalculatedRateShadowLog.cs#L36)).
- `Delta` is computed **in SQL** from the already-scaled parameters: `@NewCalculatedRate − @ExistingCalculatedRate` ([002_...sql:45-46](/Database/002_create_calculated_rate_shadow_log.sql#L45)). The C# `Delta` property ([CalculatedRateShadowComparison.cs:19](/Models/Services/CalculatedRateShadowComparison.cs#L19)) is unused.
- `ServiceCode` is never set by the recorder, so it is always NULL in the table (Low).
- Inputs per call site:

| Call site | carrierRate passed | isCwtEligible passed | existingCalculatedRate | Expected delta if only "the known bug" differs |
|---|---|---|---|---|
| GRID1Single [L610-619](/Controllers/PlantController.cs#L610) | negotiated or published (same as used) | C1 | live value | CWT: 0. Non-CWT: `published × markup%` (F1 decides whether that counts as the bug). Also data-source differences (F6). |
| GRID1Multi [L287-296](/Controllers/PlantController.cs#L287) | published | C2 (over-counted) | raw published | `markup% + fixed charges`, using a CWT flag that can be wrong. |
| GRID2 [L453-462](/Controllers/PlantController.cs#L453) | FRS net (captured before markup, [L445](/Controllers/PlantController.cs#L445)) | `false` | live value | **0**, since the formulas are identical. Any non-zero delta comes from F5 or F6. |
| LTL [L1426-1435](/Controllers/PlantController.cs#L1426) | LP `Total` | `false` | live value | 0, except when (a) the account-specific charges differ from acct 0, or (b) a charge is ≤ 0 and the live path ignores it but canonical applies it. |

- Precision note: the live values are `double` sums. A result like `120.99999999999999` becomes 120.99 if the parameter truncates, or 121.00 if it rounds (T1). **Reviewers should expect some ±$0.01 deltas that are artifacts of this, not real divergences.**

### 4.6 Persistence of Calculated Rate breakdowns

`QuoteServiceCalculation` ([QuoteServiceCalculation.cs](/Models/QuoteServiceCalculation.cs)), table [001_...sql](/Database/001_create_quote_service_calculation.sql):

| Field | UPS (`FromUps`, L29-68) | LTL (`FromLogisticsPartner`, L75-98) | Issue |
|---|---|---|---|
| `IsNegotiatedRate` | `bool.TryParse(CWT)`, which gives **NULL** for `"Yes"/"No"` (GRID1Single, GRID2) and true/false for `"True"/"False"` (GRID1Multi) | NULL | F7 |
| `CarrierRate` | negotiated if `IsNegotiatedRate == true`, else "published", which for GRID2 is GrossCharge | LP `Total` | F7. For GRID1Multi CWT it logs the negotiated rate even though the Calculated Rate used is the published one. |
| `PerPackageCharge` | the **per-package unit** charge (string) | **per-package × n** (a total) ([PlantController.cs:1422](/Controllers/PlantController.cs#L1422)) | Same column, different meaning (F13) |
| `PerShipmentCharge` | per-shipment charge; **for GRID2 it is the markup %** | per-shipment | F7 |
| `MarkupPercent` | `Plant_Surcharge` (GRID1Single). NULL for GRID1Multi and GRID2 (never set). | `fraction × 100`, and 0 when the configured value is ≤ 0 | GRID2's markup is not recorded even though it was applied. |
| `CalculatedRate` | `(decimal)CustomerRate` | `(decimal)totalCharges` | `TryToDecimal` rejects NaN, ±∞ and out-of-range values ([L100-109](/Models/QuoteServiceCalculation.cs#L100)). Good. |

Quote History (`Requests_Rate`): one row per UPS HTTP call, **including Address Validation calls** and one per plant in multi-plant mode ([UPSRequest.cs:253](/Models/Services/UPSRequest.cs#L253)). It stores the raw request and response JSON only (Carrier Rate, never Calculated Rate). The richer `LogRequest_Rate(RateResponse)` is a no-op ([Log.cs:14-262](/Models/Services/Log.cs#L14)). The GRID3 array-merging code at [PlantController.cs:480-503](/Controllers/PlantController.cs#L480) is dead as a result: its `newService`/`newCarrier`/`newRate` arrays are never assigned, and it overwrites the GRID1/GRID2 arrays. The Logistics Partner stream (`LogResultsLTL`) logs raw XML with `UserName = "TBD"`.

### 4.7 Displayed numbers (Razor and JS)

| # | Location | Formula | Notes |
|---|---|---|---|
| V1 | [ShipmentConfirmation.cshtml:212](/Views/Plant/ShipmentConfirmation.cshtml#L212), [313](/Views/Plant/ShipmentConfirmation.cshtml#L313) | "Customer Cost" = `CustomerRate.ToString("C")` | Display-only rounding |
| V2 | [L197-198](/Views/Plant/ShipmentConfirmation.cshtml#L197) | `_packageCharge = float.Parse(PerPkg) × n`, `_shipmentCharge = float.Parse(PerShp)` | Computed and never displayed. `float.Parse(null)` throws for unhandled services (F22). |
| V3 | [L202-204](/Views/Plant/ShipmentConfirmation.cshtml#L202), [300-302](/Views/Plant/ShipmentConfirmation.cshtml#L300) | Debug "Wise Profit$" = `CustomerRate − Negotiated`, "Wise Profit%" = `Profit$ / Negotiated` (`P00`) | Divides by zero when negotiated is `"0"` (shows ∞ or NaN). For non-CWT, the "profit" includes the published-vs-negotiated spread, not just Plant Charges. `_diff` is unused. |
| V4 | [L199-200](/Views/Plant/ShipmentConfirmation.cshtml#L199), [303-304](/Views/Plant/ShipmentConfirmation.cshtml#L303) | Debug footer shows the **`Ground`** markup % for UPS and UPSCWT on every service's grid | That is wrong for air services, which use other columns. The debug help misstates the markup. Low. |
| V5 | [L246](/Views/Plant/ShipmentConfirmation.cshtml#L246) | Footer: "Customer Cost = (Published or Negotiated) + Plant Surcharge" | Does not match the code: non-CWT gets no surcharge but does get per-package and per-shipment charges. |
| V6 | [L308](/Views/Plant/ShipmentConfirmation.cshtml#L308) | `plant_surcharge = CustomerRate − Published(GrossCharge)` | Unused. Would be negative. |
| V7 | [L319](/Views/Plant/ShipmentConfirmation.cshtml#L319) | GRID2 "Published" column shows GrossCharge; [L322](/Views/Plant/ShipmentConfirmation.cshtml#L322) "Acc. Chg." = negotiated − base (P3) | Labels vs the Quotes report (F23) |
| V8 | [L415-424](/Views/Plant/ShipmentConfirmation.cshtml#L415) | LTL shows the preformatted strings `TotalCost`, `RatedShipment_Surcharge` (= fraction × Total), `Plant_PerPackageCharge` (= total pkg charge), and so on | Accessorial is `decimal.ToString()`, unformatted. The accessorial-mismatch check at [L403-409](/Views/Plant/ShipmentConfirmation.cshtml#L403) compares against the string `"$0.00"`, but that value is never `"$0.00"` because it is not "C"-formatted, and the row class line is commented out anyway. Dead. |
| V9 | [L520-564](/Views/Plant/ShipmentConfirmation.cshtml#L520) | JS: highlights Negotiated when `data-cwt === "Yes"`, else Published | Matches C1's `"Yes"` values. No arithmetic. |
| V10 | [Rates/Index.cshtml](/Views/Rates/Index.cshtml) | Static policy chart: non-CWT = published, no markup, no per-carton or per-shipment. CWT = negotiated + markup. LTL = negotiated + markup. The percentages shown are placeholders ("99.9%"). | The authoritative-looking policy statement behind F1 and Q2. Not data-driven, so it can drift from `PlantCarrierCharges2017`. |

### 4.8 Reports

**Margin Report**: [ReportsController.cs:23-121](/Controllers/ReportsController.cs#L23), [MarginReport.cshtml](/Views/Reports/MarginReport.cshtml). **Dead:** it queries `Freight_Margin_VIEW`, a known-broken leftover (CONTEXT.md). With the working-tree change it now catches the failure and shows "temporarily unavailable" ([L70-74](/Controllers/ReportsController.cs#L70)). It will never show data until it is rebuilt on a working Actual Cost source. For the record, the formulas it *would* compute:
- `FreightMargin = decimal(FRGHT) − decimal(SUM(NetDue))` ([L59](/Controllers/ReportsController.cs#L59)). This is Invoice Freight Billed minus Actual Cost. `NetDue` is unqualified in the SQL, so it is unclear which side of the join it comes from, and summing across a join can multiply rows ([L32-36](/Controllers/ReportsController.cs#L32)).
- `FreightMarkupPercentage = 0` (hard-coded, although the comment says `Margin / TotalCost`). `FuelSurcharge` and `Accessorial1-3` are `0`. `Plant` is hard-coded `ALP` ([L51](/Controllers/ReportsController.cs#L51)). "Type" is hard-coded `LTL` in the view ([MarginReport.cshtml:76](/Views/Reports/MarginReport.cshtml#L76)).
- Money types are mixed: `FreightMargin` is `decimal`, `FreightMarkupPercentage`, `FuelSurcharge` and the accessorials are `float`, and billed and total cost are `string` ([Invoice.cs:16-24](/Models/Invoice.cs#L16)).
- CSV export in JS copies `innerText`, so no math ([MarginReport.cshtml:5-36](/Views/Reports/MarginReport.cshtml#L5)).

**Quotes report**: [ReportsController.cs:123-206](/Controllers/ReportsController.cs#L123). `TotalPages = ceil(count / pageSize)`. `pageSize = 0` divides by zero (double ∞, then an int cast), and `page ≤ 0` gives a negative SQL OFFSET. Low. It shows raw `TotalCharges` as "Published Rate" and the negotiated total ([L230-231](/Controllers/ReportsController.cs#L230)). Carrier Rate only: no Calculated Rate, so it cannot show what the requester was quoted. `TotalCount` includes Address Validation rows (§4.6), which show as "No rate data".

### 4.9 Dead or unreachable calculation code

- `GetLessThanTruckloadRates_M33` ([PlantController.cs:758-1081](/Controllers/PlantController.cs#L758)): its call is commented out ([L477](/Controllers/PlantController.cs#L477)). It contains a third weight formula, `pw × n + last` ([L887](/Controllers/PlantController.cs#L887)), which over-counts.
- `CalculateGroundFreight` ([L1609-1659](/Controllers/PlantController.cs#L1609)), `ValidateStateForCWT` (C4), `GetPlantUpcharges` result, the CWT, default and `NextDayAirEarlyAM` branches of `CalculateUPSRate`, the itemized-charges loop in `ParseUPSService`, `Log.LogRequest_Rate(RateResponse)`, the GRID3 log-merging code, and `Freight_Margin_VIEW` / Margin Report.
- `CalculateUPSRate` also builds an unused `RateCalculations()` and runs an unused `Plant.Charges()` query ([RateCalculations.cs:226-228](/Models/Services/RateCalculations.cs#L226)).

---

## 5. Findings

Severity: **High** = can show the requester a wrong or missing price, or invalidates the ADR 0003 rollout gate. **Medium** = wrong persisted or reporting data, or a realistic failure. **Low** = cosmetic, dead or unlikely.

| ID | Sev | Category | Finding | Evidence |
|---|---|---|---|---|
| F1 | High | Inconsistency / open question | Canonical formula (markup on everything) vs the documented 3/21/2025 rule and the Rates chart (non-CWT: no markup, no per-carton or per-shipment charges). Contradicts ADR 0003's framing of non-CWT as a bug. | [PlantController.cs:574-596](/Controllers/PlantController.cs#L574), [Rates/Index.cshtml:47-194](/Views/Rates/Index.cshtml#L47), [ADR 0003](/docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md), [CalculatedRateTests.cs:25-38](/Tests/CalculatedRateTests.cs#L25) |
| F2 | High | Bug | Multi-plant grid rates line up with service names by position; `UPS2ndDayAir` is in the order but not in `supportedServices`, so rates shift by one row. | [PlantController.cs:1676-1714](/Controllers/PlantController.cs#L1676), [Configuration.cs:138-160](/Models/Configuration.cs#L138) |
| F3 | High | Bug *(static)* | Multi-plant mode: view null dereference, KeyNotFound on CWT dictionaries, `Columns.Remove("CWT")`, and Shipment aliasing that moves GRID2/LTL to the last plant. | [ShipmentConfirmation.cshtml:5](/Views/Plant/ShipmentConfirmation.cshtml#L5), [190](/Views/Plant/ShipmentConfirmation.cshtml#L190); [PlantController.cs:209-210](/Controllers/PlantController.cs#L209), [277-278](/Controllers/PlantController.cs#L277), [324](/Controllers/PlantController.cs#L324); [RateCalculations.cs:68](/Models/Services/RateCalculations.cs#L68) |
| F4 | High | Bug *(static)* | `Convert.ToInt16` on float strings: fractional weights throw in GRID2 and GRID1Multi. | [RateCalculations.cs:216](/Models/Services/RateCalculations.cs#L216), [220](/Models/Services/RateCalculations.cs#L220), [326](/Models/Services/RateCalculations.cs#L326); callers [PlantController.cs:223-271](/Controllers/PlantController.cs#L223), [446](/Controllers/PlantController.cs#L446) |
| F5 | High | Shadow validity | Canonical GF lookup mixes the `GF` markup with `UPS` per-package and per-shipment charges. The live GRID2 formula already equals canonical, so every GRID2 delta comes from the lookup mismatch. | [RateCalculationsChargeSchedule.cs:34-38](/Models/Services/RateCalculationsChargeSchedule.cs#L34), [68-69](/Models/Services/RateCalculationsChargeSchedule.cs#L68); [RateCalculations.cs:242-245](/Models/Services/RateCalculations.cs#L242), [293-296](/Models/Services/RateCalculations.cs#L293) |
| F6 | Medium | Shadow validity / inconsistency | Two Plant Charges sources (`PlantCarrierCharges2017` vs `GetPlantCharges`), and account 0 vs the real `AcctNum` for LTL. | [Plant.cs:101](/Models/Plant.cs#L101); [RateCalculations.cs:83-86](/Models/Services/RateCalculations.cs#L83); [PlantController.cs:1113](/Controllers/PlantController.cs#L1113); [RateCalculationsChargeSchedule.cs:25](/Models/Services/RateCalculationsChargeSchedule.cs#L25), [34](/Models/Services/RateCalculationsChargeSchedule.cs#L34) |
| F7 | Medium | Reporting data | `QuoteServiceCalculation`: `IsNegotiatedRate` NULL and `CarrierRate` = published or GrossCharge for GRID1Single and GRID2; GRID2 `PerShipmentCharge` = markup %; GRID2 `MarkupPercent` NULL. | [QuoteServiceCalculation.cs:34-51](/Models/QuoteServiceCalculation.cs#L34); [UPSRequest.cs:335](/Models/Services/UPSRequest.cs#L335), [382](/Models/Services/UPSRequest.cs#L382); [PlantController.cs:450](/Controllers/PlantController.cs#L450) |
| F8 | Medium | Duplication / inconsistency | Five CWT implementations with different weight sums, service sets and package minimums. | §4.2 |
| F9 | Medium | Inconsistency | GF hard-codes freight class 65; LTL uses the user's class; the UI shows the user's class for both. | [UPSRequest.cs:648](/Models/Services/UPSRequest.cs#L648), [694](/Models/Services/UPSRequest.cs#L694); [PlantController.cs:1157](/Controllers/PlantController.cs#L1157) |
| F10 | Medium | Units / accuracy | Fixed 5×5×5 in dimensions; no dimensional weight; UPS `BillingWeight` ignored. | [UPSRequest.cs:670-678](/Models/Services/UPSRequest.cs#L670); [PlantController.cs:1261](/Controllers/PlantController.cs#L1261); [response.json:29-35](/documentation/response.json#L29) |
| F11 | Medium | Inconsistency | LTL ignores non-positive markups and charges; canonical applies them. Possible shadow deltas if any configured value is ≤ 0. | [PlantController.cs:1123-1137](/Controllers/PlantController.cs#L1123), [1388-1398](/Controllers/PlantController.cs#L1388); [CalculatedRate.cs:33-36](/Models/Services/CalculatedRate.cs#L33) |
| F12 | Medium | Reporting data | `QuoteServiceCalculation.CarrierRate` for GRID1Multi CWT = negotiated, while `CalculatedRate` there is the published rate, which looks like a markup that was never applied. | [QuoteServiceCalculation.cs:44-47](/Models/QuoteServiceCalculation.cs#L44); [PlantController.cs:223-286](/Controllers/PlantController.cs#L223) |
| F13 | Medium | Reporting data | `PerPackageCharge` holds the per-package unit charge for UPS but the total package charge (× n) for LTL. | [PlantController.cs:1422](/Controllers/PlantController.cs#L1422); [QuoteServiceCalculation.cs:63](/Models/QuoteServiceCalculation.cs#L63) |
| F14 | Medium | Inconsistency | `NextDayAirEarlyAM` is CWT-eligible and negotiated-based on the live path, yet labelled "AIR-NN, negotiated not allowed" elsewhere. | [UPSRequest.cs:340](/Models/Services/UPSRequest.cs#L340); [RateCalculations.cs:262](/Models/Services/RateCalculations.cs#L262); [UPSService.cs:51](/Models/UPSService.cs#L51) |
| F15 | Medium | Order of application | Markup is applied to the carrier's all-in total, including fuel, accessorials and (UPS) residential surcharge. That may or may not be intended (Q3). | [PlantController.cs:581-591](/Controllers/PlantController.cs#L581), [1386-1391](/Controllers/PlantController.cs#L1386); [UPSRequest.cs:333](/Models/Services/UPSRequest.cs#L333) |
| F16 | Low | Dead / misleading | `CalculateUPSRate` computes weight and CWT flags it never uses; the dead CWT branch multiplies by 0.7 while the comment says "divided by 70%". | [RateCalculations.cs:211-221](/Models/Services/RateCalculations.cs#L211), [249-250](/Models/Services/RateCalculations.cs#L249), [272-287](/Models/Services/RateCalculations.cs#L272) |
| F17 | Low | Edge case | Zero packages silently becomes 1 package / 1 lb for UPS; LTL then sends zero items. | [UPSRequest.cs:46-50](/Models/Services/UPSRequest.cs#L46); [PlantController.cs:1277](/Controllers/PlantController.cs#L1277) |
| F18 | Medium | Surcharges | LTL pickup accessorials are sent as delivery codes (RESD, LAPD, IDEL), and liftgate pickup and delivery both send `LIFT`. The quote's accessorial charges may not match the service actually requested. | [PlantController.cs:1831-1855](/Controllers/PlantController.cs#L1831) |
| F19 | Medium | Failed carrier response *(static)* | A UPS error response leaves `UPSServices` null, then `.ToList()` or `[0]` throws, so the requester sees a generic error page instead of UPS's message. | [UPSRequest.cs:407-447](/Models/Services/UPSRequest.cs#L407); [PlantController.cs:729](/Controllers/PlantController.cs#L729), [445](/Controllers/PlantController.cs#L445); [FilterConfig.cs:10](/App_Start/FilterConfig.cs#L10) |
| F20 | Medium | Default becomes 0 | A missing negotiated total becomes `"0"`, so a CWT Calculated Rate collapses to just the fixed charges with no warning. | [UPSRequest.cs:356](/Models/Services/UPSRequest.cs#L356); [PlantController.cs:581-591](/Controllers/PlantController.cs#L581) |
| F21 | Medium | Failed carrier response *(static)* | LTL: a missing fuel line throws; a `WebException` is swallowed and then a null dereference follows. | [PlantController.cs:1413](/Controllers/PlantController.cs#L1413), [1496-1513](/Controllers/PlantController.cs#L1496), [484](/Controllers/PlantController.cs#L484) |
| F22 | Low | Edge case *(static)* | GRID1Single: services outside the 7 handled ones break the view (`float.Parse(null)`, dictionary KeyNotFound). Most likely for non-US destinations. | [PlantController.cs:365-431](/Controllers/PlantController.cs#L365); [ShipmentConfirmation.cshtml:197](/Views/Plant/ShipmentConfirmation.cshtml#L197), [210](/Views/Plant/ShipmentConfirmation.cshtml#L210) |
| F23 | Low | UI vs report | "Published" means GrossCharge on the confirmation page but `TotalCharges` (net) in the Quotes report for the same GF quote. | [ShipmentConfirmation.cshtml:319](/Views/Plant/ShipmentConfirmation.cshtml#L319); [ReportsController.cs:230](/Controllers/ReportsController.cs#L230) |
| F24 | Low | Default becomes 0 | Canonical `LookUp` returns 0 for a missing or unparseable charge, so a config gap looks the same as "no charge" in shadow rows. | [RateCalculationsChargeSchedule.cs:75-83](/Models/Services/RateCalculationsChargeSchedule.cs#L75); [CalculatedRateTests.cs:54-64](/Tests/CalculatedRateTests.cs#L54) |
| F25 | Low | Display | Debug footer shows the `Ground` markup % on every grid, and the "Customer Cost =" footers do not match the code. | [ShipmentConfirmation.cshtml:199-200](/Views/Plant/ShipmentConfirmation.cshtml#L199), [246](/Views/Plant/ShipmentConfirmation.cshtml#L246), [349](/Views/Plant/ShipmentConfirmation.cshtml#L349) |
| F26 | Low | Display | "Wise Profit%" divides by the negotiated rate, which gives ∞ or NaN when that rate is 0. | [ShipmentConfirmation.cshtml:204](/Views/Plant/ShipmentConfirmation.cshtml#L204) |
| F27 | Low | Reporting | Quote History has one row per HTTP call (Address Validation included, one per plant in multi-plant), so the Quotes count is inflated. The re-run `includeLtl` guess is wrong. | [UPSRequest.cs:253](/Models/Services/UPSRequest.cs#L253); [Quotes.cshtml:164](/Views/Reports/Quotes.cshtml#L164) |
| F28 | Low | Shadow data | `CalculatedRateShadowLog.ServiceCode` is never populated. | [ShadowComparisonRecorder.cs:39-47](/Models/Services/ShadowComparisonRecorder.cs#L39) |
| F29 | Low | Dead | Margin Report formula is dead (the view is broken). Hard-coded 0% markup, fuel and accessorials, and `ALP`. | [ReportsController.cs:30-68](/Controllers/ReportsController.cs#L30) |
| F30 | Low | Dead / nonsense | `GetPlantUpcharges` adds a percent to dollars; its result is unused. | [PlantController.cs:1821-1824](/Controllers/PlantController.cs#L1821), [1091](/Controllers/PlantController.cs#L1091) |

### Duplicated logic

- **Plant Charges loading:** three separate loaders with different shapes. `Plant.Charges` (typed `double`, from the table), `RateCalculations` (a `Dictionary<string,string>` per column, from the proc), and inline `GetPlantCharges` readers in both LTL functions and `GetPlantUpcharges` ([PlantController.cs:769-785](/Controllers/PlantController.cs#L769), [1106-1122](/Controllers/PlantController.cs#L1106), [1806-1827](/Controllers/PlantController.cs#L1806)).
- **Service name to markup column mapping:** written out three times. `ApplyPlantCharges` switch ([PlantController.cs:549-572](/Controllers/PlantController.cs#L549)), `UpchargeDictionaryFor` ([RateCalculationsChargeSchedule.cs:48-73](/Models/Services/RateCalculationsChargeSchedule.cs#L48)), and the GRID1Multi switch ([PlantController.cs:220-273](/Controllers/PlantController.cs#L220)).
- **UPS service code to name:** `ParseUPSService` switch ([UPSRequest.cs:269-331](/Models/Services/UPSRequest.cs#L269)), the `ServiceCode` enum, `Configuration._UPSRateCode` ([Configuration.cs:41-60](/Models/Configuration.cs#L41)), `ReportsController.MapServiceCode` ([L287-305](/Controllers/ReportsController.cs#L287)), and the view's `serviceNameTranslations` ([ShipmentConfirmation.cshtml:21-30](/Views/Plant/ShipmentConfirmation.cshtml#L21)).
- **Total weight:** five formulas (W1, C2, C3, C4, M33 L887).
- **Markup formula:** four live formulas plus canonical (§4.4.6), as tracked by `docs/issues/0001`.

---

## 6. Open questions for the business

- **Q1 (from F1): does a non-CWT UPS shipment get a markup?** The 3/21/2025 GM decision and the rate chart say "published rate, no markup". ADR 0003 says to mark it up. Which is correct? The answer decides whether GRID1Single's non-CWT shadow deltas are "the known bug" or a change in pricing policy.
- **Q2: for non-CWT, should per-carton and per-shipment charges apply?** The chart says "None", but the code adds them from the `UPS` row today. Are those DB values actually 0?
- **Q3: what should the markup apply to?** Today it applies to the carrier's total, which includes fuel surcharge, accessorials and (now) the UPS residential surcharge. Should fuel and residential be passed through at cost, or marked up?
- **Q4: should multi-plant quotes be marked up?** GRID1Multi shows the raw carrier rate and multi-plant Ground Freight is raw too. Is multi-plant meant as a carrier-cost comparison tool, or as customer pricing?
- **Q5: is Next Day Air Early AM CWT-eligible, and may it use the negotiated rate?** Code disagrees with itself (F14).
- **Q6: should UPS Saver (65) count as ground CWT?** C1 excludes it; C2 and C3 include it.
- **Q7: is there an upper weight bound for air CWT?** The comment says "<200 lbs (over 200 is LTL?)"; the code has none.
- **Q8: should UPS Ground Freight use the user's freight class, or is 65 deliberate?** The Sep 2025 comments suggest 65 was a deliberate default for LTL, yet LTL now honors the user's pick.
- **Q9: should Quotes use real package dimensions?** Is the 5×5×5 placeholder acceptable given that Invoices will reflect dimensional weight?
- **Q10: are Plant Charges customer-account-specific?** `GetPlantCharges` takes `@AcctNumber`, but only LTL passes a real account. Should UPS paths honor account-specific charges too? The shadow module should use whatever the answer is.
- **Q11: which Plant Charges table is the source of truth,** `PlantCarrierCharges2017` or whatever `GetPlantCharges` reads? Should they always agree?
- **Q12: can a markup or charge be negative (a discount) or zero?** LTL ignores non-positive values; canonical applies them.
- **Q13: where should rounding to cents happen?** Options: per component, on the final price, or never in code. And with which rule: half-up (common for invoices) or banker's? Today it happens only in display and in SQL.
- **Q14: how should `Unknown` address classifications be quoted?** Carried over from 0002: a disclaimer, or a contingency buffer?
- **Q15 (Margin Report): what should Actual Cost include,** and should markup % be `Margin / Actual Cost` (the code comment) or `Margin / Freight Billed`?

## 7. Technical questions to verify (not business)

- **T1:** Does the `System.Data.SqlClient` version this project uses **round or truncate** a `decimal` whose scale is larger than `SqlParameter.Scale = 2`? This affects every persisted Calculated Rate and shadow `Delta` (§4.5). Check with a one-row insert of `1.235` against a dev database.
- **T2:** The SQL for `GetPlantCharges`, `PlantCarrierCharges2017`, `LogRequest_Rate` and `LogResultsLTL` is **not in the repo** (only `Database/001` and `002` are), so their calculations and column semantics could not be audited. Can they be scripted into `Database/`?
- **T3:** Reproduce F3, F4, F19 and F21 against a dev environment. They come from static reading, and `HandleErrorAttribute` ([FilterConfig.cs:10](/App_Start/FilterConfig.cs#L10)) would turn each into a generic error page.

## 8. Out of scope, but noticed

Database and carrier-API credentials are hard-coded in source ([Configuration.cs:37-39](/Models/Configuration.cs#L37), [96-113](/Models/Configuration.cs#L96), [ReportsController.cs:30](/Controllers/ReportsController.cs#L30)). `PlantNames` swaps Anderson and Portland relative to `PlantLocations` ([Configuration.cs:129-130](/Models/Configuration.cs#L129) vs [68-69](/Models/Configuration.cs#L68)). Several SQL queries are built by string concatenation ([Plant.cs:34](/Models/Plant.cs#L34), [101](/Models/Plant.cs#L101); [PlantController.cs:1199](/Controllers/PlantController.cs#L1199)). None of these affect calculations. They are listed only so they aren't lost.
