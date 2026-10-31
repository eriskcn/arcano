using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class FilesController : ApiControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] Guid? folderId, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        using var stream = file.OpenReadStream();
        var dto = new FileUploadDto(stream, file.FileName, file.ContentType, file.Length, folderId);

        var result = await _fileService.UploadFileAsync(dto, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] Guid? folderId = null, [FromQuery] string? view = null, [FromQuery] string? q = null, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var result = await _fileService.ListFilesAsync(skip, take, folderId, view, q, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _fileService.DownloadFileAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return ToError(result.Error);
        }

        var (content, contentType, fileName) = result.Value;
        return File(content, contentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.DeleteFileAsync(id, cancellationToken));

    [HttpPut("{id:guid}/name")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameFileRequest request, CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.RenameFileAsync(id, request.Name, cancellationToken));

    [HttpPut("{id:guid}/star")]
    public async Task<IActionResult> Star(Guid id, [FromBody] StarFileRequest request, CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.SetStarredAsync(id, request.IsStarred, cancellationToken));

    [HttpGet("trash")]
    public async Task<IActionResult> ListTrash([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var result = await _fileService.ListTrashAsync(skip, take, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.RestoreFileAsync(id, cancellationToken));

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> DeletePermanently(Guid id, CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.PermanentlyDeleteFileAsync(id, cancellationToken));

    [HttpDelete("trash")]
    public async Task<IActionResult> EmptyTrash(CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.EmptyTrashAsync(cancellationToken));

    [HttpPut("{id:guid}/folder")]
    public async Task<IActionResult> MoveFolder(Guid id, [FromBody] MoveFileRequest request, CancellationToken cancellationToken) =>
        ToNoContent(await _fileService.MoveFileAsync(id, request.FolderId, cancellationToken));

    [HttpPost("{id:guid}/copy")]
    public async Task<IActionResult> Copy(Guid id, [FromBody] CopyFileRequest request, CancellationToken cancellationToken)
    {
        var result = await _fileService.CopyFileAsync(id, request.FolderId, cancellationToken);
        return result.IsFailure ? ToError(result.Error) : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    private IActionResult ToNoContent(ArcanoCore.Domain.Common.Result result) =>
        result.IsFailure ? ToError(result.Error) : NoContent();

    private IActionResult ToError(ArcanoCore.Domain.Common.Error error) =>
        error.Code == "File.NotFound" ? NotFound(error) : BadRequest(error);

    public record RenameFileRequest(string Name);

    public record StarFileRequest(bool IsStarred);

    // Null FolderId moves the file to the root.
    public record MoveFileRequest(Guid? FolderId);
}
