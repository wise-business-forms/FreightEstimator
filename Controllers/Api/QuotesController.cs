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
    /// carrier is called for it. UPS Ground Freight (GRID2, #19) and UPS parcel (GRID1 single plant, #20) are implemented.
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

            QuoteUps(response, request, consumer, quoteId, plantCode);
            return Ok(response);
        }

        /// <summary>
        /// Quotes the UPS services that were asked for and are available (parcel, GRID 1; and Ground Freight, GRID 2),
        /// and puts the results, UPS's status and any warning into the response. Does nothing, and calls no carrier,
        /// when none is. UPS is one carrier in the response however many of its calls were made: its status is the
        /// worst of them.
        /// </summary>
        private static void QuoteUps(QuoteResponse response, QuoteRequest request, Consumer consumer, Guid quoteId, string plantCode)
        {
            List<QuotedService> parcel = response.Services.Where(s => s.Carrier == ServiceCatalog.Ups && s.Service != RateCalculationsChargeSchedule.GroundFreightServiceName && s.Availability == "available").ToList();
            QuotedService groundFreight = response.Services.FirstOrDefault(s => s.Service == RateCalculationsChargeSchedule.GroundFreightServiceName && s.Availability == "available");
            if (parcel.Count == 0 && groundFreight == null) return;

            UpsQuoteRequest upsRequest = new UpsQuoteRequest
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
            };
            var upsService = new UpsEstimationService();
            var statuses = new List<CarrierStatus>();
            var messages = new List<string>();
            string classification = null;

            if (parcel.Count > 0)
            {
                upsRequest.ParcelServiceNames = parcel.Select(s => s.Service).ToList();
                UpsParcelEstimate estimate = upsService.EstimateParcel(upsRequest);
                statuses.Add(estimate.Status);
                if (estimate.Message != null) messages.Add(estimate.Message);
                classification = classification ?? estimate.AddressClassification;
                foreach (UpsParcelServiceEstimate quoted in estimate.Services)
                {
                    QuotedService service = parcel.FirstOrDefault(s => s.Service == quoted.ServiceName);
                    if (service != null) Fill(service, quoted.CalculatedRate);
                }
            }

            if (groundFreight != null)
            {
                UpsGroundFreightEstimate estimate = upsService.EstimateGroundFreight(upsRequest);
                statuses.Add(estimate.Status);
                if (estimate.Message != null) messages.Add(estimate.Message);
                classification = classification ?? estimate.AddressClassification;
                if (estimate.Status == CarrierStatus.Ok) Fill(groundFreight, estimate.CalculatedRate);
            }

            CarrierStatus worst = statuses.Contains(CarrierStatus.Unavailable) ? CarrierStatus.Unavailable
                : statuses.Contains(CarrierStatus.Error) ? CarrierStatus.Error : CarrierStatus.Ok;
            response.Carriers.Add(new CarrierResult
            {
                Carrier = ServiceCatalog.Ups,
                Status = worst == CarrierStatus.Ok ? "ok" : worst == CarrierStatus.Unavailable ? "unavailable" : "error",
                Message = messages.Count == 0 ? null : string.Join(" ", messages.Distinct())
            });
            response.AddressClassification = classification;

            if (string.Equals(classification, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                response.Warnings.Add(new ApiWarning { Code = "addressClassificationUnknown", Message = UnknownClassificationWarning });
            }
        }

        private static void Fill(QuotedService service, CalculatedRate rate)
        {
            service.CalculatedRate = rate.RoundedAmount;
            service.CarrierRate = rate.CarrierRate;
            service.PlantCharges = new PlantChargesApplied
            {
                MarkupPercent = rate.MarkupPercent,
                PerPackageCharge = rate.PerPackageCharge,
                PerShipmentCharge = rate.PerShipmentCharge
            };
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
