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
    /// Requests a Quote. This is the skeleton (#18): it authenticates the Consumer, validates the request,
    /// generates a Quote ID, logs the Quote, and reports every service as not available, because no Calculated
    /// Rate path has been cut over to the unified module yet (CONTEXT.md: Service Availability). It makes no
    /// carrier call.
    /// </summary>
    [ApiKeyAuthorize]
    [RoutePrefix("api/v1")]
    public class QuotesController : ApiController
    {
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

            return Ok(new QuoteResponse
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
            });
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
