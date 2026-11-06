using System;
using System.Collections.Generic;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// The Plant a CostPlus request ships from, by its from-address state. A CostPlus request names no Plant, only a
    /// from-address, but a Quote needs one, so the adapter maps the state through this explicit table: the console app's table,
    /// carried over as reviewed data (ADR 0005).
    ///
    /// This is a KNOWING EXCEPTION to "don't infer the Plant" (CONTEXT.md; ADR 0004): accepted because CostPlus cannot change
    /// this phase. It goes away, and this class with it, when CostPlus sends a plant code. Any state not in the table is
    /// rejected with a clear error, never guessed.
    /// </summary>
    internal static class CostPlusPlantMapping
    {
        /// <summary>Bump when the table changes, so a change is a reviewed, visible act.</summary>
        public const string Version = "2026-11-06";

        private static readonly Dictionary<string, string> PlantByState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GA"] = "ALP",
            ["PA"] = "BUT",
            ["IN"] = "FTW",
            ["SC"] = "PDT",
            ["ME"] = "POR"
        };

        /// <summary>The Plant for a from-state, or false with the reason to report to CostPlus when there is none.</summary>
        public static bool TryPlantFor(string fromState, out string plantCode, out string problem)
        {
            plantCode = null;
            problem = null;

            string state = fromState == null ? "" : fromState.Trim();
            if (state.Length == 0)
            {
                problem = "Ship-from state is missing";
                return false;
            }
            if (!PlantByState.TryGetValue(state, out plantCode))
            {
                problem = "Ship-from state '" + state + "' is not mapped to a Plant";
                return false;
            }
            return true;
        }
    }
}
