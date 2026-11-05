# Freight Estimator

Estimates freight cost for outbound shipments (parcel via UPS, and LTL via our Logistics Partner) before they ship, and reports on how those estimates hold up against reality.

## Language

**Quote**:
A rate estimate the app produces for a not-yet-shipped shipment: the shipment details (origin, destination, packages, service options) plus the Carrier Rate(s) and resulting Calculated Rate(s) returned for it.
_Avoid_: Shipment (the `Shipment` class conflates the input form, calculation options, and results — it is not this concept on its own), Rate Request.

**Carrier Rate**:
The raw rate returned by a carrier's API (UPS or the Logistics Partner) for one service, before the app applies any markup, plant charges, or CWT adjustment. This is the only value the app currently persists in Quote History.
_Avoid_: Published Rate, Negotiated Rate — these are UPS-specific sub-types of Carrier Rate, not synonyms for it.

**Calculated Rate**:
The final price the app displays to the requester for a Quote, after applying Plant Charges (markup, per-package/per-shipment charges) and a CWT adjustment to a Carrier Rate. Persisted per service, per Quote, in `QuoteServiceCalculation` — additive to (not a replacement for) the existing raw Carrier Rate logging.
_Avoid_: Quoted Price, Total Charges.

Known gap: computed four different, disagreeing ways depending on which code path produced it — UPS GRID1 single-plant, UPS GRID1 multi-plant, UPS Ground Freight (GRID2), and LTL each have their own formula (see `docs/issues/0001`). A fix is underway: a single `CalculatedRate.Apply(CarrierRate, ChargeSchedule, RatingContext)` module is being migrated in one call site at a time, in shadow mode (see `docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md`). Until a call site is fully migrated, Calculated Rate is still persisted faithfully as that path computes it today, warts and all.

**Shadow Mode**:
Running the new Calculated Rate formula alongside a call site's existing one for every real Quote, without showing its answer to anyone. The requester still sees the existing price. Both values are recorded as a **shadow row**, one per Quote and service, with the difference between them. A call site goes live on the new formula only after a sample of its differences has been reviewed and every one explained (ADR 0003). The shadow rows are throwaway: dropped once all call sites are live.
_Avoid_: Dry run, A/B test (nobody is shown the new value).

**Plant Charges**:
The per-plant, per-service rates a plant applies on top of a Carrier Rate: a markup percentage, a per-package charge, and a per-shipment charge. Looked up today via `Plant.Charges()` and the `Plant_Surcharge`/`Plant_PerPackageCharge`/`Plant_ShipmentCharge` fields; one of the inputs to Calculated Rate.
_Avoid_: Surcharge (that's specifically the markup-percentage component of Plant Charges, not a synonym for the whole thing).

**Address Classification**:
UPS's own determination of whether a delivery address is Residential, Commercial, or Unknown, returned by UPS's Address Validation call and stored per Quote as `Shipment.Address_Classification`. A Residential classification triggers a UPS residential delivery surcharge; as of `docs/issues/0002`, that classification is passed into the Rate request so the surcharge is quoted, not just invoiced later. `Unknown` means UPS's own address database doesn't know in advance — the surcharge can still show up for the first time on the invoice, with nothing on the quote to have warned of it.

**Logistics Partner**:
The company that handles our LTL (Less-Than-Truckload) shipments. Referred to as "TransportInsight" or "TI" throughout the code (`TICharge`, `Configuration.TransportInsightRateUrl`, `shopLessThanTruckloadResponseTransportInsight`).
_Avoid_: TI, TransportInsight — fine as code-level/technical names, but "Logistics Partner" is the business-facing term to use in reporting and conversation.

**UPS Ground Freight**:
UPS's service for heavier shipments, quoted separately from UPS parcel and priced with its own Plant Charges. It is not one of the UPS Base Services, and it is not **UPS Ground**. The code calls it GRID2.
_Avoid_: Ground (ambiguous with UPS Ground), GRID2 in business-facing writing.

**UPS Base Services**:
The seven UPS parcel services the app quotes: UPS Ground, 3 Day Select, 2nd Day Air, 2nd Day Air AM, Next Day Air Saver, Next Day Air and Next Day Air Early AM. Quoted together in one UPS request, and priced as UPS parcel (code name GRID1). UPS Ground Freight is a separate service and is not among them.

**UPS Ground**:
One of the seven UPS Base Services: UPS's standard parcel ground service. **Not UPS Ground Freight**, which is a different service quoted and priced separately. Always write the full name of each; never shorten either to "Ground".
_Avoid_: Ground Freight (for this service), Ground (alone, for either service).

**Quote History**:
The retained record of past Quotes. Currently split into two disconnected streams that must be unified for reporting: UPS quotes logged via `Requests_Rate` (stored proc `LogRequest_Rate`), and Logistics Partner quotes logged separately via `LogResultsLTL`. The Logistics Partner stream never records a real requester — its `UserName` parameter is hardcoded to `"TBD"`.

**Quote ID**:
A unique identifier generated by the app itself at the moment a Quote is requested, before either carrier's logging call runs. It is passed to both the UPS and Logistics Partner logging paths so a Quote's per-service Calculated Rate breakdown can be attached to it consistently, regardless of which carrier produced it. Deliberately not a database identity column, since UPS and Logistics Partner logging today have no shared key at all (UPS gets an identity back from `LogRequest_Rate`; Logistics Partner logging returns nothing).

**Consumer**:
Another application, outside the Freight Estimator web UI, that requests Quotes through the Quote API instead of the Plant page. Identified by its own API key, so each Quote can be traced to the application that asked for it.
CostPlus (the FileMaker application) is a Consumer too, but it is identified by a fixed identity rather than an API key, because it asks through a request table, not through the Quote API.
_Avoid_: Client, user (a requester on the Plant page is a user; a Consumer is software).

**Service Availability**:
Whether a given carrier service can be quoted through the Quote API yet. A service is available only once its Calculated Rate path has been cut over to the unified module (ADR 0003); until then the API reports it as not available rather than returning a number the business already considers wrong. Reported per service, separately from whether the carrier itself responded.
_Avoid_: Provisional rate, beta service.

**Invoice** (also: **Actual Cost**):
The real, final freight cost for a shipment after it has actually shipped, sourced from the ERP data warehouse (`ARSAL_T1` / `Freight_Margin_VIEW`). This is the ground truth a Quote is judged against once a shipment has moved — as opposed to a Quote, which is only ever an estimate.

Known gap: `Freight_Margin_VIEW` is currently broken — it selects from `dbo.Freight_Margin`, which doesn't exist in `DataWarehouse` and hasn't since the view was created (2025-05-01). Confirmed a leftover from a one-off report; the DB admin needs to drop it. Until then, Actual Cost isn't queryable through this view at all; `ARSAL_T1` alone only carries the bill-to address, not ship-to, so it can't be matched to a Quote by destination either.

**Reference Number**:
An identifier that does not exist yet in the Quote flow. It must be captured on a Quote at request time and must also be findable on that shipment's eventual Invoice, so the two can be matched later for accuracy reporting. Quotes made before this field exists cannot be matched to an Invoice — there is no way to backfill history.
_Avoid_: Order Number, PO Number — not used until we confirm which ERP field the Reference Number is actually expected to match.

**Rate Variance**:
The difference between a Quote's Calculated Rate and the eventual Invoice/Actual Cost for the same shipment (once matched via Reference Number). Reported as a plain figure for now — there is no automatic thresholding or flagging of "problem" variances yet.

**Margin Report**:
An existing, separate report (`ReportsController.MarginReport`) showing freight profit — Invoice Freight Billed minus Actual Cost — per invoice. Distinct in purpose from Quote History / Rate Variance reporting: Margin Report is about profitability after the fact, not about whether the app's estimates were accurate.

**Match Group**:
UPS Quotes that sent UPS the same inputs, and so should get the same Carrier Rate unless UPS or its agreement with us changed. Quotes are in the same group when they share the plant, ship-to zip, package count, package weights (each rounded up to a whole pound, since that's how UPS bills, then sorted), and whether `ResidentialAddressIndicator` was sent. Rates are compared per service within a group. Keyed on what was *sent* to UPS, not on Address Classification, so a residential address quoted before `docs/issues/0002` groups with commercial Quotes.
_Avoid_: Similar quotes, duplicate quotes. Membership is an exact match on the criteria above, not a similarity score.

**Rate Change**:
A difference of at least 1 cent in a Carrier Rate or Calculated Rate between one Quote and the previous Quote in the same Match Group for the same service. Each Rate Change gets one or more attribution labels: **UPS price change** (Published changed), **UPS negotiated change** (Negotiated changed, Published didn't), **Our charges changed** (Plant Charges component changed), **Our calculation changed** (every input the same, Calculated Rate different, e.g. an ADR 0003 cutover), or **Unattributable** (no Calculated Rate could be matched). Specified in issue #11.
_Avoid_: Rate Variance. That compares a Quote with its Invoice; a Rate Change compares a Quote with an earlier Quote.

Known gap: `Requests_Rate` doesn't carry the Quote ID, so a UPS Quote's Calculated Rate is attached by approximate match (same plant, service and Carrier Rate, logged within 30 seconds after the request) and left blank when the match is ambiguous. Calculated Rates only exist from when `QuoteServiceCalculation` went live.
