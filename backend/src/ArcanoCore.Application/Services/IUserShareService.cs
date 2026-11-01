using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Application.Services;

public interface IUserShareService
{
    // Created is false when the share already existed (idempotent).
    Task<Result<(UserShareDto Share, bool Created)>> CreateAsync(UserShareRequestDto dto, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<UserShareRecipientDto>>> ListForItemAsync(Guid? fileId, Guid? folderId, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<IncomingShareDto>>> ListIncomingAsync(CancellationToken cancellationToken = default);
    Task<Result<SharedChildrenDto>> GetChildrenAsync(Guid shareId, Guid? folderId, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid shareId, Guid fileId, CancellationToken cancellationToken = default);
    Task<Result<string>> GetFolderZipNameAsync(Guid shareId, Guid folderId, CancellationToken cancellationToken = default);
    Task<Result> WriteFolderZipAsync(Guid shareId, Guid folderId, Stream output, CancellationToken cancellationToken = default);
}
