using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Application.Services;

public interface IUploadService
{
    Task<Result<UploadStartedDto>> StartAsync(UploadStartDto dto, CancellationToken cancellationToken = default);
    Task<Result<UploadStatusDto>> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> UploadPartAsync(Guid id, int partNumber, Stream content, long length, CancellationToken cancellationToken = default);
    Task<Result<FileResponseDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> AbortAsync(Guid id, CancellationToken cancellationToken = default);
}
