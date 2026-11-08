using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace AuthenticationServer.Models.Services
{
    /// <summary>An application that may call the Quote API (CONTEXT.md: Consumer).</summary>
    public class Consumer
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Looks up a Consumer by its API key. Only the SHA-256 of a key is stored (dbo.Consumer.ApiKeyHash), so a
    /// leak of the table does not leak working keys. Keys are long random strings, so a plain hash is enough.
    /// </summary>
    public static class ConsumerStore
    {
        /// <summary>The active Consumer holding this key, or null if the key is unknown or has been revoked.</summary>
        public static Consumer Find(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return null;

            using (SqlConnection conn = new SqlConnection(Configuration.FreightEstimatorSqlConnection))
            using (SqlCommand cmd = new SqlCommand("SELECT ConsumerId, Name FROM dbo.Consumer WHERE ApiKeyHash = @hash AND IsActive = 1", conn))
            {
                cmd.Parameters.Add("@hash", SqlDbType.VarBinary, 32).Value = Hash(apiKey);
                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    return reader.Read() ? new Consumer { Id = reader.GetInt32(0), Name = reader.GetString(1) } : null;
                }
            }
        }

        public static byte[] Hash(string apiKey)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            }
        }
    }
}
