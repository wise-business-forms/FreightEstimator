using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// Refuses a request that has no authenticated user name, before anything else (anti-forgery included) runs, so nothing is
    /// written under an anonymous identity. The app has no authentication of its own: IIS Windows authentication supplies the user.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    internal sealed class RequireSignedInUserAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var identity = context.HttpContext.User?.Identity;
            if (identity == null || !identity.IsAuthenticated || string.IsNullOrWhiteSpace(identity.Name))
                context.Result = new ObjectResult("This needs a signed-in Windows user. Nothing was saved.") { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
