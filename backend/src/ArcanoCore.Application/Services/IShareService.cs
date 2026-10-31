using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Application.Services;

public interface IShareService
{
    Task<Result<ShareLinkDto>> CreateLinkAsync(ShareLinkRequestDto dto, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ShareLinkDto>>> ListLinksAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<Result> RevokeLinkAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<SharedFileInfoDto>> GetSharedFileInfoAsync(string token, CancellationToken cancellationToken = default);

    // Anonymous: the token is the only credential.
    Task<Result<(Stream Content, string ContentType, string FileName)>> OpenSharedFileAsync(string token, CancellationToken cancellationToken = default);
}
