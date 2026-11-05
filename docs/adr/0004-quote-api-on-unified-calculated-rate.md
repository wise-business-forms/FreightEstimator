# Expose estimation through an in-app Quote API built on the unified Calculated Rate module

Other applications need the same freight estimates the Plant page produces. We will add a versioned Web API (`POST /api/v1/quotes`) inside this web app. It is backed by an estimation service extracted out of `PlantController.ShipmentConfirmation`, and the Plant page becomes that service's first caller.

We considered exposing each path's current Calculated Rate as-is, and building the API as a separate implementation beside an untouched `PlantController`. We rejected both: Calculated Rate is computed four disagreeing ways today (`docs/issues/0001`), so freezing those numbers into a contract other applications depend on would turn the later ADR 0003 cutover into a breaking change; and two copies of the logic would drift the first time either is edited. A separate service was also rejected for now, because it adds deployment and configuration with no benefit until the extracted seam exists.

Consequences a future reader should not be surprised by:

- **Services appear incrementally.** The API quotes a service only once its path has been cut over to the unified `CalculatedRate.Apply` module (ADR 0003). Until then that service is reported as not available (Service Availability), not given a provisional number. GRID2 is first.
- **The extraction must be behaviour-preserving.** The Plant page must show the same numbers before and after, guarded by characterization tests (`Tests/TestSet-01.json` plus new ones) written before the move.
- **Every API call is a Quote.** It gets a Quote ID (ADR 0002) and requires a caller-supplied Reference Number (ADR 0001), and is attributed to the Consumer's API key.
- **The contract does not carry the form's quirks.** Packages are an explicit list, and zero or negative weights are rejected. Dimensions, account-specific Plant Charges and multi-plant rating are out of v1; they can be added later without breaking callers.
- **Partial results.** Each carrier and each service carries its own status; a carrier failure does not fail the whole request.
- **Rounding is decided once**, in the unified module, on the final Calculated Rate: half-up to the cent (half away from zero), confirmed by finance in #13. This matches what the Plant page displays today. Components are returned unrounded. Legacy persisted Calculated Rates were truncated by the SQL parameter, not rounded, so they can sit a cent below what was displayed (see #13).
