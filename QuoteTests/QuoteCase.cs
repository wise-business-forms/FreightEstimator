using Newtonsoft.Json.Linq;

namespace FreightEstimator.QuoteTests
{
    /// <summary>One recorded UPS quote (Tests/Recorded/quotes/*.json): what was asked and what UPS answered.</summary>
    public class QuoteCase
    {
        public int RequestId;
        public string Plant;
        public string Kind;              // "parcel" (GRID 1) or "groundfreight" (GRID 2)
        public int Packages;
        public float PackageWeight, LastPackageWeight;
        public string ShipToState, ShipToZip;
        public string ResponseJson;

        public override string ToString() => RequestId + " " + Plant + " " + Kind + " " + Packages + " pkgs";

        public static IEnumerable<QuoteCase> All(string kind)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Recorded", "quotes");
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                JObject o = JObject.Parse(File.ReadAllText(file));
                if ((string)o["kind"] != kind) continue;
                yield return new QuoteCase
                {
                    RequestId = (int)o["requestId"],
                    Plant = (string)o["plant"],
                    Kind = (string)o["kind"],
                    Packages = (int)o["packages"],
                    PackageWeight = (float)o["packageWeight"],
                    LastPackageWeight = (float)o["lastPackageWeight"],
                    ShipToState = (string)o["shipToState"],
                    ShipToZip = (string)o["shipToZip"],
                    ResponseJson = o["response"].ToString(Newtonsoft.Json.Formatting.None)
                };
            }
        }
    }
}
