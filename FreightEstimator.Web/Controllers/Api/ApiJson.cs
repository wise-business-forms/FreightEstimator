using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace AuthenticationServer.Controllers.Api
{
    /// <summary>
    /// Writes a Quote API body with Json.NET's default settings, as the Web API host did. The contract names are on the
    /// models (JsonProperty), so it does not depend on how this host is configured, and it leaves the Plant page's own
    /// serialization alone.
    /// </summary>
    public static class ApiJson
    {
        public static ContentResult Result(int statusCode, object body) => new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json; charset=utf-8",
            Content = JsonConvert.SerializeObject(body)
        };
    }
}
