using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests
{
    /// <summary>One recorded carrier quote (Tests/Recorded/quotes/*.json): what was asked and what the carrier answered.</summary>
    public class QuoteCase
    {
        public string CaseId;            // the fixture's file name, and the name of its expected file
        public string Plant;
        public string Kind;              // "parcel" (GRID 1), "groundfreight" (GRID 2) or "ltl" (GRID 3, Logistics Partner)
        public int Packages;
        public float PackageWeight, LastPackageWeight;
        public int FreightClass;         // LTL only
        public string ShipToState, ShipToZip;
        public string ResponseText;      // the carrier's response body: UPS JSON or Logistics Partner XML

        public override string ToString() => CaseId + " " + Plant + " " + Kind + " " + Packages + " pkgs";

        public static IEnumerable<QuoteCase> All(string kind)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes");
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                JObject o = JObject.Parse(File.ReadAllText(file));
                if ((string)o["kind"] != kind) continue;
                yield return new QuoteCase
                {
                    CaseId = Path.GetFileNameWithoutExtension(file),
                    Plant = (string)o["plant"],
                    Kind = (string)o["kind"],
                    Packages = (int)o["packages"],
                    PackageWeight = (float)o["packageWeight"],
                    LastPackageWeight = (float)o["lastPackageWeight"],
                    FreightClass = o["freightClass"] == null ? 0 : (int)o["freightClass"],
                    ShipToState = (string)o["shipToState"],
                    ShipToZip = (string)o["shipToZip"],
                    ResponseText = o["responseXml"] != null ? (string)o["responseXml"] : o["response"].ToString(Newtonsoft.Json.Formatting.None)
                };
            }
        }
    }
}
