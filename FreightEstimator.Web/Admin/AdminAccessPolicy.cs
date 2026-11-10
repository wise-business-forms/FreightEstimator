using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AuthenticationServer.Admin
{
    /// <summary>
    /// The admin access rule (<see cref="AdminAccess"/>) bound to this app's settings: Admin:AllowedUsers, and whether this
    /// is a Development run. One place for the controller's gate and the navbar to ask, so they can never disagree.
    /// </summary>
    internal class AdminAccessPolicy
    {
        private readonly string[] _allowedUsers;
        private readonly bool _isDevelopment;

        public AdminAccessPolicy(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _allowedUsers = configuration.GetSection("Admin:AllowedUsers").Get<string[]>();
            _isDevelopment = environment.IsDevelopment();
        }

        public bool IsAllowed(ClaimsPrincipal user) => AdminAccess.IsAllowed(user?.Identity?.Name, _allowedUsers, _isDevelopment);
    }
}
