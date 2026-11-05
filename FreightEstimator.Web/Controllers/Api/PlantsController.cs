using AuthenticationServer.Models.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AuthenticationServer.Controllers.Api
{
    /// <summary>The plants a Quote can be requested for.</summary>
    [ApiKeyAuthorize]
    [Route("api/v1")]
    public class PlantsController : ControllerBase
    {
        /// <summary>Lists the valid plant codes, so Consumers need not hardcode a list that drifts.</summary>
        [HttpGet("plants")]
        public IActionResult Get() => ApiJson.Result(StatusCodes.Status200OK, PlantDirectory.ValidCodes());
    }
}
