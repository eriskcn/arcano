using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class ShareController : ApiControllerBase
{
    private readonly IShareService _shareService;

    public ShareController(IShareService shareService)
    {
        _shareService = shareService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(ShareLinkRequestDto dto, CancellationToken cancellationToken)
    {
        var result = await _shareService.CreateLinkAsync(dto, cancellationToken);
        return Respond(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid fileId, CancellationToken cancellationToken)
    {
        var result = await _shareService.ListLinksAsync(fileId, cancellationToken);
        return Respond(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var result = await _shareService.RevokeLinkAsync(id, cancellationToken);
        return result.IsFailure ? ErrorResult(result.Error) : NoContent();
    }

    private IActionResult Respond<T>(ArcanoCore.Domain.Common.Result<T> result) =>
        result.IsFailure ? ErrorResult(result.Error) : Ok(result.Value);

    private IActionResult ErrorResult(ArcanoCore.Domain.Common.Error error) =>
        error.Code.EndsWith(".NotFound") ? NotFound(error) : BadRequest(error);
}

// Anonymous download by share token. File() with a file name sends Content-Disposition: attachment,
// so shared HTML/SVG is never rendered in this origin.
[AllowAnonymous]
public class PublicController : ApiControllerBase
{
    private readonly IShareService _shareService;

    public PublicController(IShareService shareService)
    {
        _shareService = shareService;
    }

    [HttpGet("{token}/info")]
    public async Task<IActionResult> Info(string token, CancellationToken cancellationToken)
    {
        var result = await _shareService.GetSharedFileInfoAsync(token, cancellationToken);
        return result.IsFailure ? NotFound(result.Error) : Ok(result.Value);
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken cancellationToken)
    {
        var result = await _shareService.OpenSharedFileAsync(token, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.Code == "Share.NotFound" ? NotFound(result.Error) : BadRequest(result.Error);
        }

        var (content, contentType, fileName) = result.Value;
        return File(content, contentType, fileName);
    }
}
