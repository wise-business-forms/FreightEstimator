using System;
using System.Linq;
using AuthenticationServer.Models.Api;
using AuthenticationServer.Models.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AuthenticationServer.Controllers.Api
{
    /// <summary>
    /// Authenticates a Quote API call by its per-application API key, sent in the X-Api-Key header. A missing,
    /// unknown or revoked key is a 401. Otherwise the Consumer is attached to the request, so the Quote can be
    /// traced to the application that asked for it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ApiKeyAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        public const string HeaderName = "X-Api-Key";
        private const string ConsumerItem = "FreightEstimator.Consumer";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // The first value, as the Web API host took it; a repeated header is not a comma-joined key.
            string apiKey = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();

            Consumer consumer = ConsumerStore.Find(string.IsNullOrEmpty(apiKey) ? null : apiKey);
            if (consumer == null)
            {
                context.Result = ApiJson.Result(StatusCodes.Status401Unauthorized, new UnauthorizedResponse());
                return;
            }

            context.HttpContext.Items[ConsumerItem] = consumer;
        }

        /// <summary>The Consumer a request was authenticated as. Only valid after this filter has run.</summary>
        public static Consumer ConsumerOf(HttpContext context) => (Consumer)context.Items[ConsumerItem];
    }
}
