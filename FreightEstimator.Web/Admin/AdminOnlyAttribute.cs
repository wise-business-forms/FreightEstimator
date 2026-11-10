using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace AuthenticationServer.Admin
{
    /// <summary>
    /// Gates an action or controller to the users in Admin:AllowedUsers (ADR 0006). Anyone else, signed in or not, gets 403.
    /// Asking a visitor to sign in is IIS's job (Anonymous Authentication off for the admin path, docs/aspnetcore-iis.md),
    /// so this gate never challenges: a missing sign-in fails closed instead of depending on an authentication handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    internal class AdminOnlyAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            if (context.HttpContext.RequestServices.GetRequiredService<AdminAccessPolicy>().IsAllowed(context.HttpContext.User)) return;

            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
