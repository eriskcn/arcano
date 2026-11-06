using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class FoldersController : ApiControllerBase
{
    private readonly IFolderService _folderService;

    public FoldersController(IFolderService folderService)
    {
        _folderService = folderService;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FolderRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _folderService.CreateFolderAsync(request, cancellationToken);
        return result.IsFailure ? ToErrorResult(result.Error) : Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? parentId, CancellationToken cancellationToken)
    {
        var result = await _folderService.ListFoldersAsync(parentId, cancellationToken);
        return result.IsFailure ? ToErrorResult(result.Error) : Ok(result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FolderRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _folderService.UpdateFolderAsync(id, request, cancellationToken);
        return result.IsFailure ? ToErrorResult(result.Error) : Ok(result.Value);
    }

    [HttpPut("{id:guid}/star")]
    public async Task<IActionResult> Star(Guid id, [FromBody] StarFolderRequest request, CancellationToken cancellationToken)
    {
        var result = await _folderService.SetStarredAsync(id, request.IsStarred, cancellationToken);
        return result.IsFailure ? ToErrorResult(result.Error) : NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _folderService.DeleteFolderAsync(id, cancellationToken);
        return result.IsFailure ? ToErrorResult(result.Error) : NoContent();
    }

    [Authorize(AuthenticationSchemes = DownloadTicket.SessionOrTicket)]
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        // Validate and cap before the response starts, so errors can still be returned as JSON.
        var name = await _folderService.GetZipNameAsync(id, cancellationToken);
        if (name.IsFailure)
        {
            return ToErrorResult(name.Error);
        }

        // Nothing is written before the cap/ownership checks pass, so a failure can still return JSON.
        // shortcut: ZipArchive writes synchronously and Kestrel forbids that by default; this blocks one thread per
        // download, so use a streaming zip writer if many large zips run at once.
        HttpContext.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO = true;
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = name.Value }.ToString();
        var result = await _folderService.WriteZipAsync(id, Response.Body, cancellationToken);
        if (result.IsFailure)
        {
            Response.ContentType = null;
            Response.Headers.ContentDisposition = default;
            return ToErrorResult(result.Error);
        }

        return new EmptyResult();
    }

    // Same checks and caps as Download, so an error shows in the app before the browser starts saving.
    [HttpPost("{id:guid}/download/ticket")]
    public async Task<IActionResult> Ticket(Guid id, [FromServices] IConfiguration configuration, CancellationToken cancellationToken)
    {
        var name = await _folderService.GetZipNameAsync(id, cancellationToken);
        return name.IsFailure ? ToErrorResult(name.Error) : DownloadTicketResult(configuration);
    }

    public record StarFolderRequest(bool IsStarred);

    private IActionResult ToErrorResult(Error error) =>
        error.Code == "Folder.NotFound" ? NotFound(error) : BadRequest(error);
}
