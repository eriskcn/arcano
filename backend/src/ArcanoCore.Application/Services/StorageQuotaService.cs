using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

public class StorageQuotaService : IStorageQuotaService
{
    public const long DefaultLimitBytes = 5368709120; // 5 GiB

    private readonly IApplicationDbContext _dbContext;
    private readonly long _limitBytes;

    // The limit is passed in (see AddApplication) because this project does not reference IConfiguration.
    public StorageQuotaService(IApplicationDbContext dbContext, long limitBytes)
    {
        _dbContext = dbContext;
        _limitBytes = limitBytes;
    }

    public async Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        // Trashed files (IsDeleted = true) still occupy storage; purged files are hard-deleted, so every row counts.
        var used = await _dbContext.FileMetadatas
            .IgnoreQueryFilters()
            .Where(f => f.OwnerId == ownerId)
            .SumAsync(f => f.FileSize, cancellationToken);
        var own = await _dbContext.UserProfiles.Where(p => p.UserId == ownerId).Select(p => p.QuotaBytes).FirstOrDefaultAsync(cancellationToken);
        return new StorageUsageDto(used, own ?? _limitBytes);
    }

    public async Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default)
    {
        var usage = await GetUsageAsync(ownerId, cancellationToken);
        return usage.UsedBytes + additionalBytes > usage.LimitBytes
            ? Result.Failure(new Error("Storage.QuotaExceeded", "Not enough storage space."))
            : Result.Success();
    }
}
