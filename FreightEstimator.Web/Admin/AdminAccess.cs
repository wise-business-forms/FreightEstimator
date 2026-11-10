using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.Admin
{
    /// <summary>
    /// Who may open the admin area (ADR 0006): only the users named in Admin:AllowedUsers, as DOMAIN\user, compared without
    /// regard to case. Nobody when none are named, so a missing setting cannot open the area up. Development skips the
    /// check (Kestrel needs extra setup for Windows authentication, and the FreightEstimator-only database guard already
    /// keeps a Development run off production data).
    /// </summary>
    internal static class AdminAccess
    {
        public static bool IsAllowed(string userName, IEnumerable<string> allowedUsers, bool isDevelopment)
        {
            if (isDevelopment) return true;
            if (string.IsNullOrWhiteSpace(userName) || allowedUsers == null) return false;

            string user = userName.Trim();
            return allowedUsers
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Any(entry => string.Equals(entry.Trim(), user, StringComparison.OrdinalIgnoreCase));
        }
    }
}
