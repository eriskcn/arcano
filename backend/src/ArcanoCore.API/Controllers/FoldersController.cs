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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _folderService.DeleteFolderAsync(id, cancellationToken);
        return result.IsFailure ? ToErrorResult(result.Error) : NoContent();
    }

    private IActionResult ToErrorResult(Error error) =>
        error.Code == "Folder.NotFound" ? NotFound(error) : BadRequest(error);
}
