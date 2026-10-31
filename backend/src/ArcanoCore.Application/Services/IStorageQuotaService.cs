using System;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Application.Services;

public interface IStorageQuotaService
{
    Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default);
    Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default);
}
