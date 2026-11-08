using System;
using Microsoft.Extensions.Configuration;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// CostPlus:Poller:Mode (#36): "Shadow" (the default) runs beside the console app, claims nothing, writes no
    /// result rows, and records its own result next to the console app's for review (ADR 0005). "Live" answers requests, and must be set explicitly. Only matters when CostPlus:Poller:Enabled is true.
    /// </summary>
    internal static class CostPlusPollerMode
    {
        public static bool IsShadow(IConfiguration configuration) =>
            !string.Equals(configuration["CostPlus:Poller:Mode"], "Live", StringComparison.OrdinalIgnoreCase);
    }
}
