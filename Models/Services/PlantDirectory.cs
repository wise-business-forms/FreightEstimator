using System.Collections.Generic;
using System.Data.SqlClient;

namespace AuthenticationServer.Models.Services
{
    /// <summary>
    /// The plant codes a Quote can be requested for: active plants that have a UPS shipping number, the same rule
    /// Plant.Plants() applies for the Plant page. Kept separate because Plant.Plants() never disposes its
    /// connection, which is not something an API open to every Consumer should lean on.
    /// </summary>
    public static class PlantDirectory
    {
        public static List<string> ValidCodes()
        {
            var codes = new List<string>();
            using (SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection))
            using (SqlCommand cmd = new SqlCommand("SELECT DISTINCT PlantCode FROM Plants WHERE Active = 'Y' AND upsShippingNumber IS NOT NULL ORDER BY PlantCode", conn))
            {
                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) codes.Add(reader.GetString(0));
                }
            }
            return codes;
        }
    }
}
