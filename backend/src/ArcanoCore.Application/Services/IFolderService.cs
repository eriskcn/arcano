using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Application.Services;

public interface IFolderService
{
    Task<Result<FolderDto>> CreateFolderAsync(FolderRequestDto dto, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<FolderDto>>> ListFoldersAsync(Guid? parentId, CancellationToken cancellationToken = default);
    Task<Result<FolderDto>> UpdateFolderAsync(Guid id, FolderRequestDto dto, CancellationToken cancellationToken = default);
    Task<Result> DeleteFolderAsync(Guid id, CancellationToken cancellationToken = default);
}
