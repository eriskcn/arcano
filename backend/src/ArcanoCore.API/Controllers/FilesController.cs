using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

public class FilesController : ApiControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        using var stream = file.OpenReadStream();
        var dto = new FileUploadDto(stream, file.FileName, file.ContentType, file.Length);

        var result = await _fileService.UploadFileAsync(dto, cancellationToken);
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
            if (result.Error.Code == "File.NotFound")
            {
                return NotFound(result.Error);
            }
            return BadRequest(result.Error);
        }

        var (content, contentType, fileName) = result.Value;
        return File(content, contentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _fileService.DeleteFileAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "File.NotFound")
            {
                return NotFound(result.Error);
            }
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}
