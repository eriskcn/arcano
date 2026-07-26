using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArcanoCore.Application.Services;

public class FileBloomFilter : IBloomFilter<Guid>
{
    private readonly BloomFilter<Guid> _filter;
    private readonly IServiceProvider _serviceProvider;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);

    public FileBloomFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        // Capacity e.g. 100,000 files, false positive rate 1%
        _filter = new BloomFilter<Guid>(100000, 0.01, guid =>
        {
            byte[] bytes = guid.ToByteArray();
            ulong hash1 = BitConverter.ToUInt64(bytes, 0);
            ulong hash2 = BitConverter.ToUInt64(bytes, 8);
            return (hash1, hash2);
        });
    }

    public async Task AddAsync(Guid item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        _filter.Add(item);
    }

    public async Task<bool> MightContainAsync(Guid item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return _filter.MightContain(item);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return;

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var fileIds = await dbContext.FileMetadatas
                .AsNoTracking()
                .Where(f => !f.IsDeleted)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in fileIds)
            {
                _filter.Add(id);
            }

            _isInitialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }
}
