using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ArcanoCore.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    // A download ticket for the GET at this request's path without its last segment ("/ticket").
    protected IActionResult DownloadTicketResult(IConfiguration configuration)
    {
        var path = (Request.PathBase + Request.Path).Value!.TrimEnd('/');
        return Ok(new { ticket = JwtTokens.CreateDownloadTicket(configuration, User, path[..path.LastIndexOf('/')]) });
    }
}
