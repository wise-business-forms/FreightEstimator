using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using AuthenticationServer.Models.Api;
using AuthenticationServer.Models.Services;
using Newtonsoft.Json;

namespace AuthenticationServer.Controllers.Api
{
    /// <summary>
    /// Requests a Quote: authenticates the Consumer, validates the request, generates a Quote ID, logs the Quote,
    /// and quotes each requested service that is available (CONTEXT.md: Service Availability). A service whose
    /// Calculated Rate path has not been cut over to the unified module is reported as not available, and no
    /// carrier is called for it. UPS Ground Freight (GRID2) is the first path to be available (#19).
    /// A carrier that does not answer, or answers with an error, does not fail the Quote: it is a 200 with that
    /// carrier's status set and everything else as it was.
    /// </summary>
    [ApiKeyAuthorize]
    [RoutePrefix("api/v1")]
    public class QuotesController : ApiController
    {
        /// <summary>How long UPS gets to answer each call before it is reported as unavailable.</summary>
        private const int UpsTimeoutMs = 10000;

        private const string UnknownClassificationWarning =
            "UPS could not classify the delivery address as residential or commercial. A residential delivery surcharge " +
            "may still appear on the Invoice with nothing on this Quote to warn of it.";

        [HttpPost]
        [Route("quotes")]
        public async Task<IHttpActionResult> Post()
        {
            // Read the body as sent: it is what gets logged, and a body that is not JSON is the Consumer's mistake, a 400.
            string json = await Request.Content.ReadAsStringAsync();
            QuoteRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<QuoteRequest>(json);
            }
            catch (JsonException)
            {
                request = null;
            }

            List<string> plantCodes = PlantDirectory.ValidCodes();
            List<ApiError> errors = QuoteRequestValidator.Validate(request, plantCodes);
            if (errors.Count > 0)
            {
                return Content(HttpStatusCode.BadRequest, new ValidationFailedResponse { Errors = errors });
            }

            Consumer consumer = ApiKeyAuthorizeAttribute.ConsumerOf(Request);
            string plantCode = plantCodes.First(c => string.Equals(c, request.PlantCode.Trim(), StringComparison.OrdinalIgnoreCase));
            string referenceNumber = request.ReferenceNumber.Trim();

            // Every call is a new Quote with its own ID: no idempotency, so re-quoting is never mistaken for a retry.
            Guid quoteId = Guid.NewGuid();
            QuoteLog.Record(quoteId, consumer.Id, referenceNumber, plantCode, json);

            var response = new QuoteResponse
            {
                QuoteId = quoteId,
                ReferenceNumber = referenceNumber,
                PlantCode = plantCode,
                Services = ServicesWanted(request).Select(s => new QuotedService
                {
                    Service = s.Name,
                    Carrier = s.Carrier,
                    Availability = ServiceCatalog.IsAvailable(s) ? "available" : "notAvailable"
                }).ToList()
            };

            QuoteGroundFreight(response, request, consumer, quoteId, plantCode);
            return Ok(response);
        }

        /// <summary>
        /// Quotes UPS Ground Freight if it was asked for and is available, and puts the result, the carrier's status
        /// and any warning into the response. Does nothing, and calls no carrier, otherwise.
        /// </summary>
        private static void QuoteGroundFreight(QuoteResponse response, QuoteRequest request, Consumer consumer, Guid quoteId, string plantCode)
        {
            QuotedService groundFreight = response.Services.FirstOrDefault(s => s.Service == RateCalculationsChargeSchedule.GroundFreightServiceName && s.Availability == "available");
            if (groundFreight == null) return;

            UpsGroundFreightEstimate estimate = new UpsEstimationService().EstimateGroundFreight(new UpsQuoteRequest
            {
                PlantCode = plantCode,
                QuoteId = quoteId,
                ReferenceNumber = request.ReferenceNumber.Trim(),
                Consumer = consumer.Name,
                UserName = consumer.Name,
                AccountNumber = "0",
                Address = request.Destination.Address,
                City = request.Destination.City,
                State = request.Destination.State,
                Zip = request.Destination.PostalCode,
                Country = string.IsNullOrWhiteSpace(request.Destination.Country) ? "US" : request.Destination.Country,
                PackageWeights = request.Packages.Select(p => (float)p.Weight.Value).ToList(),
                TimeoutMs = UpsTimeoutMs
            });

            response.Carriers.Add(new CarrierResult
            {
                Carrier = ServiceCatalog.Ups,
                Status = estimate.Status == CarrierStatus.Ok ? "ok" : estimate.Status == CarrierStatus.Unavailable ? "unavailable" : "error",
                Message = estimate.Message
            });
            response.AddressClassification = estimate.AddressClassification;

            if (string.Equals(estimate.AddressClassification, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                response.Warnings.Add(new ApiWarning { Code = "addressClassificationUnknown", Message = UnknownClassificationWarning });
            }

            if (estimate.Status == CarrierStatus.Ok)
            {
                CalculatedRate rate = estimate.CalculatedRate;
                groundFreight.CalculatedRate = rate.RoundedAmount;
                groundFreight.CarrierRate = rate.CarrierRate;
                groundFreight.PlantCharges = new PlantChargesApplied
                {
                    MarkupPercent = rate.MarkupPercent,
                    PerPackageCharge = rate.PerPackageCharge,
                    PerShipmentCharge = rate.PerShipmentCharge
                };
            }
        }

        /// <summary>The services asked for, in catalog order and without repeats, or the whole catalog if none were named.</summary>
        private static IEnumerable<CatalogService> ServicesWanted(QuoteRequest request)
        {
            if (request.Services == null || request.Services.Count == 0) return ServiceCatalog.All;

            var wanted = request.Services.Select(ServiceCatalog.Find).ToList();
            return ServiceCatalog.All.Where(wanted.Contains);
        }
    }
}
