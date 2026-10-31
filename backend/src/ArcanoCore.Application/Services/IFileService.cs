using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Application.Services;

public interface IFileService
{
    Task<Result<FileResponseDto>> UploadFileAsync(FileUploadDto dto, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<FileListItemDto>>> ListFilesAsync(int skip, int take, Guid? folderId = null, string? view = null, string? query = null, CancellationToken cancellationToken = default);
    Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> DeleteFileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> RenameFileAsync(Guid id, string name, CancellationToken cancellationToken = default);
    Task<Result> SetStarredAsync(Guid id, bool isStarred, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<FileListItemDto>>> ListTrashAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<Result> RestoreFileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> PermanentlyDeleteFileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> EmptyTrashAsync(CancellationToken cancellationToken = default);
    Task<Result> MoveFileAsync(Guid id, Guid? folderId, CancellationToken cancellationToken = default);
    Task<Result<FileResponseDto>> CopyFileAsync(Guid id, Guid? folderId, CancellationToken cancellationToken = default);
}
