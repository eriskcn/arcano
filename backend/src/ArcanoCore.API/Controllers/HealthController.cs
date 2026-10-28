using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

public class HealthController : ApiControllerBase
{
    [HttpGet]
    [Route("/health")]
    public IActionResult Health()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }
}
