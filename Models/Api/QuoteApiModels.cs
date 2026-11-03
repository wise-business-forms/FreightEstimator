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
    }

    /// <summary>One requested service and whether it could be quoted (CONTEXT.md: Service Availability).</summary>
    public class QuotedService
    {
        [JsonProperty("service")] public string Service { get; set; }
        [JsonProperty("carrier")] public string Carrier { get; set; }

        /// <summary>"notAvailable" until the service's path has been cut over to the unified Calculated Rate module.</summary>
        [JsonProperty("availability")] public string Availability { get; set; }
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
