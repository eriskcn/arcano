using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace ArcanoCore.API.Controllers;

// Owner side of user-to-user sharing.
[Authorize]
[EnableRateLimiting("user-api")]
public class UserSharesController : ApiControllerBase
{
    private readonly IUserShareService _shares;

    public UserSharesController(IUserShareService shares)
    {
        _shares = shares;
    }

    [EnableRateLimiting("user-share-create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserShareRequestDto dto, CancellationToken cancellationToken)
    {
        var result = await _shares.CreateAsync(dto, cancellationToken);
        if (result.IsFailure) return ShareErrors.ToResult(this, result.Error);
        return result.Value.Created ? StatusCode(StatusCodes.Status201Created, result.Value.Share) : Ok(result.Value.Share);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? fileId, [FromQuery] Guid? folderId, CancellationToken cancellationToken)
    {
        var result = await _shares.ListForItemAsync(fileId, folderId, cancellationToken);
        return result.IsFailure ? ShareErrors.ToResult(this, result.Error) : Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _shares.DeleteAsync(id, cancellationToken);
        return result.IsFailure ? ShareErrors.ToResult(this, result.Error) : NoContent();
    }
}

// Recipient side. File() with a file name sends Content-Disposition: attachment, like FilesController.
[Authorize]
[EnableRateLimiting("user-api")]
public class SharedController : ApiControllerBase
{
    private readonly IUserShareService _shares;

    public SharedController(IUserShareService shares)
    {
        _shares = shares;
    }

    [HttpGet]
    public async Task<IActionResult> Incoming(CancellationToken cancellationToken)
    {
        var result = await _shares.ListIncomingAsync(cancellationToken);
        return result.IsFailure ? ShareErrors.ToResult(this, result.Error) : Ok(result.Value);
    }

    [HttpGet("{shareId:guid}/children")]
    public async Task<IActionResult> Children(Guid shareId, [FromQuery] Guid? folderId = null, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        var result = await _shares.GetChildrenAsync(shareId, folderId, Math.Max(0, skip), Math.Clamp(take, 1, 100), cancellationToken);
        return result.IsFailure ? ShareErrors.ToResult(this, result.Error) : Ok(result.Value);
    }

    [HttpGet("{shareId:guid}/files/{fileId:guid}/download")]
    public async Task<IActionResult> DownloadFile(Guid shareId, Guid fileId, CancellationToken cancellationToken)
    {
        var result = await _shares.DownloadFileAsync(shareId, fileId, cancellationToken);
        if (result.IsFailure) return ShareErrors.ToResult(this, result.Error);

        var (content, contentType, fileName) = result.Value;
        return File(content, contentType, fileName, enableRangeProcessing: true);
    }

    [HttpGet("{shareId:guid}/folders/{folderId:guid}/download")]
    public async Task<IActionResult> DownloadFolder(Guid shareId, Guid folderId, CancellationToken cancellationToken)
    {
        // Validate and cap before the response starts, so errors can still be returned as JSON.
        var name = await _shares.GetFolderZipNameAsync(shareId, folderId, cancellationToken);
        if (name.IsFailure) return ShareErrors.ToResult(this, name.Error);

        // shortcut: same blocking synchronous zip write as FoldersController; use a streaming zip writer if many large zips run at once.
        HttpContext.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO = true;
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = name.Value }.ToString();
        var result = await _shares.WriteFolderZipAsync(shareId, folderId, Response.Body, cancellationToken);
        if (result.IsFailure)
        {
            Response.ContentType = null;
            Response.Headers.ContentDisposition = default;
            return ShareErrors.ToResult(this, result.Error);
        }

        return new EmptyResult();
    }
}

internal static class ShareErrors
{
    public static IActionResult ToResult(ControllerBase c, Error error) => error.Code switch
    {
        "UserShare.NotFound" or "Folder.NotFound" => c.NotFound(error),
        "UserShare.Unauthorized" => c.Unauthorized(error),
        _ => c.BadRequest(error)
    };
}
