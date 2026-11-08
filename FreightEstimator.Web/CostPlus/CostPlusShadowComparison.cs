using System.Collections.Generic;
using System.Linq;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// A result row as FileMaker would read it, and the comparison of two of them: the console app's and the poller's (#36,
    /// ADR 0005). Stored as JSON next to each other so every divergence can be reviewed.
    /// </summary>
    internal static class CostPlusShadowComparison
    {
        public class Line
        {
            public string Service;
            public decimal Rate;
            public string Note;
        }

        public class Result
        {
            public List<Line> Lines = new List<Line>();
            public string ErrorCode, ErrorDescription;
            public bool? ClassifiedResidential;
            public string SubstitutedZip;
        }

        /// <summary>What differs between the two results, one sentence each. Empty when they agree on everything FileMaker reads.</summary>
        public static List<string> Differences(Result console, Result poller)
        {
            var differences = new List<string>();

            if (console.ErrorCode != poller.ErrorCode)
                differences.Add("Error code: console app " + Show(console.ErrorCode) + ", poller " + Show(poller.ErrorCode));
            else if (console.ErrorDescription != poller.ErrorDescription)
                differences.Add("Error text: console app " + Show(console.ErrorDescription) + ", poller " + Show(poller.ErrorDescription));

            if (console.Lines.Count != poller.Lines.Count)
                differences.Add("Result lines: console app " + console.Lines.Count + ", poller " + poller.Lines.Count);

            for (int i = 0; i < System.Math.Min(console.Lines.Count, poller.Lines.Count); i++)
            {
                Line c = console.Lines[i], p = poller.Lines[i];
                if (c.Service != p.Service)
                    differences.Add("Line " + (i + 1) + " service: console app " + Show(c.Service) + ", poller " + Show(p.Service));
                else if (c.Rate != p.Rate)
                    differences.Add("Line " + (i + 1) + " (" + (c.Service ?? "no service") + ") rate: console app " + c.Rate + ", poller " + p.Rate + " (poller - console app = " + (p.Rate - c.Rate) + ")");
                if (c.Note != p.Note)
                    differences.Add("Line " + (i + 1) + " note: console app " + Show(c.Note) + ", poller " + Show(p.Note));
            }

            if (console.ClassifiedResidential != poller.ClassifiedResidential)
                differences.Add("ClassifiedResidential: console app " + Show(console.ClassifiedResidential) + ", poller " + Show(poller.ClassifiedResidential));
            if (console.SubstitutedZip != poller.SubstitutedZip)
                differences.Add("SubstitutedZip: console app " + Show(console.SubstitutedZip) + ", poller " + Show(poller.SubstitutedZip));

            return differences;
        }

        private static string Show(object value) => value == null ? "(none)" : "\"" + value + "\"";
    }
}
