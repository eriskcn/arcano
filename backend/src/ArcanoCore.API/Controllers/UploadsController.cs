using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class UploadsController : ApiControllerBase
{
    private readonly IUploadService _uploadService;

    public UploadsController(IUploadService uploadService)
    {
        _uploadService = uploadService;
    }

    [HttpPost]
    public async Task<IActionResult> Start(UploadStartDto dto, CancellationToken cancellationToken) =>
        Respond(await _uploadService.StartAsync(dto, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken) =>
        Respond(await _uploadService.GetStatusAsync(id, cancellationToken));

    [HttpPut("{id:guid}/parts/{partNumber:int}")]
    public async Task<IActionResult> UploadPart(Guid id, int partNumber, CancellationToken cancellationToken)
    {
        if (Request.ContentLength == null)
        {
            return BadRequest(new Error("Upload.BadPart", "Content-Length is required."));
        }

        var result = await _uploadService.UploadPartAsync(id, partNumber, Request.Body, Request.ContentLength.Value, cancellationToken);
        return result.IsFailure ? ErrorResult(result.Error) : NoContent();
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken) =>
        Respond(await _uploadService.CompleteAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Abort(Guid id, CancellationToken cancellationToken)
    {
        var result = await _uploadService.AbortAsync(id, cancellationToken);
        return result.IsFailure ? ErrorResult(result.Error) : NoContent();
    }

    private IActionResult Respond<T>(Result<T> result) =>
        result.IsFailure ? ErrorResult(result.Error) : Ok(result.Value);

    private IActionResult ErrorResult(Error error) =>
        error.Code == "Upload.NotFound" || error.Code == "Folder.NotFound" ? NotFound(error) : BadRequest(error);
}
