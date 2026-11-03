using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using AuthenticationServer.Models.Api;
using AuthenticationServer.Models.Services;

namespace AuthenticationServer.Controllers.Api
{
    /// <summary>
    /// Authenticates a Quote API call by its per-application API key, sent in the X-Api-Key header. A missing,
    /// unknown or revoked key is a 401. Otherwise the Consumer is attached to the request, so the Quote can be
    /// traced to the application that asked for it.
    /// </summary>
    public class ApiKeyAuthorizeAttribute : AuthorizationFilterAttribute
    {
        public const string HeaderName = "X-Api-Key";
        private const string ConsumerProperty = "FreightEstimator.Consumer";

        public override void OnAuthorization(HttpActionContext actionContext)
        {
            string apiKey = null;
            if (actionContext.Request.Headers.TryGetValues(HeaderName, out var values))
            {
                apiKey = values.FirstOrDefault();
            }

            Consumer consumer = ConsumerStore.Find(apiKey);
            if (consumer == null)
            {
                actionContext.Response = actionContext.Request.CreateResponse(HttpStatusCode.Unauthorized, new UnauthorizedResponse());
                return;
            }

            actionContext.Request.Properties[ConsumerProperty] = consumer;
        }

        /// <summary>The Consumer a request was authenticated as. Only valid after this filter has run.</summary>
        public static Consumer ConsumerOf(HttpRequestMessage request) => (Consumer)request.Properties[ConsumerProperty];
    }
}
