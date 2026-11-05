using System.Collections.Generic;
using System.Web.Http;
using AuthenticationServer.Models.Services;

namespace AuthenticationServer.Controllers.Api
{
    /// <summary>The plants a Quote can be requested for.</summary>
    [ApiKeyAuthorize]
    [RoutePrefix("api/v1")]
    public class PlantsController : ApiController
    {
        /// <summary>Lists the valid plant codes, so Consumers need not hardcode a list that drifts.</summary>
        [HttpGet]
        [Route("plants")]
        public IEnumerable<string> Get()
        {
            return PlantDirectory.ValidCodes();
        }
    }
}
