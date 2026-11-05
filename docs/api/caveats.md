# Quote API: what the numbers mean

For people building against `POST /api/v1/quotes`. The field-by-field contract is in [openapi.yaml](openapi.yaml); this page explains the three things that most often surprise a first integration.

## "Not available" is not the same as "unavailable"

A Quote response says two different things, in two different places.

| Where | Value | Means |
|---|---|---|
| `services[].availability` | `notAvailable` | The API cannot quote this service **yet**. The carrier was not called and there is no rate. This is the same on every call until the service is switched on. |
| `services[].availability` | `available` | The API quotes this service. |
| `carriers[].status` | `ok` | The carrier answered with usable rates on this call. |
| `carriers[].status` | `unavailable` | The carrier did not answer **on this call** (a timeout, or no connection). Trying again may work. |
| `carriers[].status` | `error` | The carrier answered, but with an error or with something unusable (for example: no service between those locations, or a rate of zero). Retrying the same request is unlikely to help. |

So an `available` service can still come back with no `calculatedRate`: look at its carrier's `status` and `message`. A carrier failure never turns the whole request into an error; you still get `200`, the Quote is still recorded, and the other carriers' services are unaffected.

Services are switched on one at a time, after each has been checked against real traffic. Do not hardcode which services are available: ask for the ones you want and read `availability`. A service that is `notAvailable` today can become `available` without any change to your integration.

## Rounding

`calculatedRate` is the final price, **rounded once, half-up to the cent** (a half cent rounds away from zero). `carrierRate` and everything in `plantCharges` are **not** rounded: they are the figures as they went into the calculation.

The calculation is the same for every service:

```
calculatedRate = carrierRate
               + carrierRate × markupPercent / 100
               + perPackageCharge × number of packages
               + perShipmentCharge
```

`markupPercent` is a percentage (`5` means 5%). If you recompute the price from the breakdown, round only the final result, or you can land a cent away from `calculatedRate`.

## Which rate you are given

* **UPS services** are priced from the rate UPS returns for the shipment. For larger shipments UPS's negotiated (hundredweight) rate applies; for smaller ones the published rate does. You do not choose this and it is not reported: `carrierRate` is the rate that was used.
* **LTL** is a single service. The Logistics Partner returns several carriers' options; the API gives you the **cheapest** one, after our charges. LTL is rated at `freightClass` from your request, or class 65 when you send none. The pickup date is not part of the request: LTL is rated for pickup on the next business day (weekends skipped, holidays not).

## The `Unknown` address classification warning

UPS classifies a delivery address as `Residential`, `Commercial` or `Unknown`, and the response reports it as `addressClassification`. A residential delivery carries a surcharge. When UPS already knows the address is residential, the surcharge is in the rate you are given.

When UPS answers `Unknown`, it cannot tell until the package is picked up, so **the surcharge is not in the Quote and may still appear on the Invoice** (commonly $50 to $100 or more). The response then carries a warning:

```json
{ "code": "addressClassificationUnknown", "message": "UPS could not classify the delivery address ..." }
```

The rate is still returned; the warning does not withhold it. Treat the Quote for such a shipment as a lower bound, and consider telling your own users or adding a buffer.

## Other things to know

* **Every call is a new Quote.** There is no idempotency: sending the same request twice records two Quotes with two `quoteId`s.
* **`referenceNumber` is required** and must also be findable on the carrier Invoice. That is how a Quote is later compared with what was billed.
* **Packages are listed one by one**, each with a weight in pounds greater than zero. Dimensions, account-specific charges and rating from several plants at once are not part of v1.
* **UPS parcel rates cover at most 50 packages** per request; above that, the UPS carrier comes back as `error` with a message saying so.
* **A timeout is 10 seconds** for each carrier call.
