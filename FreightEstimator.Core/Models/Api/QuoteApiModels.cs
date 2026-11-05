using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuthenticationServer.Models.Api
{
    // The Quote API contract, v1 (docs/adr/0004-quote-api-on-unified-calculated-rate.md). Names are fixed with
    // JsonProperty so the contract does not depend on how the host happens to be configured. Packages are an
    // explicit list with a weight each; dimensions, account numbers and multi-plant rating are not part of v1.

    public class QuoteRequest
    {
        /// <summary>The Plant the shipment ships from. Required; never inferred from the Consumer.</summary>
        [JsonProperty("plantCode")] public string PlantCode { get; set; }

        /// <summary>The Consumer's own number for the shipment, which must also be findable on its Invoice (ADR 0001). Required.</summary>
        [JsonProperty("referenceNumber")] public string ReferenceNumber { get; set; }

        [JsonProperty("destination")] public Destination Destination { get; set; }
        [JsonProperty("packages")] public List<PackageRequest> Packages { get; set; }

        /// <summary>The services wanted, by name. Omitted or empty means every service in the catalog.</summary>
        [JsonProperty("services")] public List<string> Services { get; set; }

        /// <summary>Optional, for LTL.</summary>
        [JsonProperty("freightClass")] public int? FreightClass { get; set; }
    }

    public class Destination
    {
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("city")] public string City { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("postalCode")] public string PostalCode { get; set; }
        [JsonProperty("country")] public string Country { get; set; }
    }

    public class PackageRequest
    {
        /// <summary>In pounds. Must be greater than zero.</summary>
        [JsonProperty("weight")] public decimal? Weight { get; set; }
    }

    public class QuoteResponse
    {
        /// <summary>Generated for every call, so two identical calls are two Quotes (ADR 0002).</summary>
        [JsonProperty("quoteId")] public Guid QuoteId { get; set; }
        [JsonProperty("referenceNumber")] public string ReferenceNumber { get; set; }
        [JsonProperty("plantCode")] public string PlantCode { get; set; }
        [JsonProperty("services")] public List<QuotedService> Services { get; set; }

        /// <summary>UPS's classification of the delivery address (Residential, Commercial or Unknown), when a carrier was asked and could say.</summary>
        [JsonProperty("addressClassification", NullValueHandling = NullValueHandling.Ignore)] public string AddressClassification { get; set; }

        /// <summary>Things a Consumer should know about the numbers above. Empty when there are none.</summary>
        [JsonProperty("warnings")] public List<ApiWarning> Warnings { get; set; } = new List<ApiWarning>();

        /// <summary>One entry per carrier that was asked. A carrier that failed does not fail the Quote: the rest is still returned.</summary>
        [JsonProperty("carriers")] public List<CarrierResult> Carriers { get; set; } = new List<CarrierResult>();
    }

    public class ApiWarning
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    public class CarrierResult
    {
        [JsonProperty("carrier")] public string Carrier { get; set; }

        /// <summary>"ok", "unavailable" (did not answer) or "error" (answered with an error).</summary>
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)] public string Message { get; set; }
    }

    /// <summary>The Plant Charges that were applied to a Carrier Rate, as applied: not rounded.</summary>
    public class PlantChargesApplied
    {
        [JsonProperty("markupPercent")] public decimal MarkupPercent { get; set; }
        [JsonProperty("perPackageCharge")] public decimal PerPackageCharge { get; set; }
        [JsonProperty("perShipmentCharge")] public decimal PerShipmentCharge { get; set; }
    }

    /// <summary>One requested service and whether it could be quoted (CONTEXT.md: Service Availability).</summary>
    public class QuotedService
    {
        [JsonProperty("service")] public string Service { get; set; }
        [JsonProperty("carrier")] public string Carrier { get; set; }

        /// <summary>
        /// "notAvailable" until the service's path has been cut over to the unified Calculated Rate module, then "available".
        /// An available service whose carrier failed is still "available", with no rate: the carrier's status says why.
        /// </summary>
        [JsonProperty("availability")] public string Availability { get; set; }

        /// <summary>The final price, rounded half-up to the cent. Absent unless the service was quoted.</summary>
        [JsonProperty("calculatedRate", NullValueHandling = NullValueHandling.Ignore)] public decimal? CalculatedRate { get; set; }

        /// <summary>What the carrier charged, before our Plant Charges. Not rounded.</summary>
        [JsonProperty("carrierRate", NullValueHandling = NullValueHandling.Ignore)] public decimal? CarrierRate { get; set; }
        [JsonProperty("plantCharges", NullValueHandling = NullValueHandling.Ignore)] public PlantChargesApplied PlantCharges { get; set; }
    }

    public class ApiError
    {
        /// <summary>The request field at fault, e.g. "referenceNumber" or "packages[1].weight".</summary>
        [JsonProperty("field")] public string Field { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    public class ValidationFailedResponse
    {
        [JsonProperty("error")] public string Error { get; set; } = "validationFailed";
        [JsonProperty("errors")] public List<ApiError> Errors { get; set; }
    }

    public class UnauthorizedResponse
    {
        [JsonProperty("error")] public string Error { get; set; } = "unauthorized";
        [JsonProperty("message")] public string Message { get; set; } = "A valid API key is required in the X-Api-Key header.";
    }
}
