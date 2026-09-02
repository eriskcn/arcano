using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Common;
using ArcanoCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArcanoCore.Infrastructure.Security;

public class EmailBloomFilter : IBloomFilter<string>
{
    private readonly BloomFilter<string> _filter;
    private readonly IServiceProvider _serviceProvider;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);

    public EmailBloomFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        // 100,000 capacity, 1% false positive rate
        _filter = new BloomFilter<string>(100000, 0.01, email =>
        {
            ulong hash1 = GetFnv1aHash(email, 14695981039346656037UL);
            ulong hash2 = GetFnv1aHash(email, 5683262657731236573UL);
            return (hash1, hash2);
        });
    }

    private static ulong GetFnv1aHash(string input, ulong offsetBasis)
    {
        ulong hash = offsetBasis;
        const ulong prime = 1099511628211UL;
        foreach (char c in input)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }

    public async Task AddAsync(string item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        _filter.Add(item.ToUpperInvariant());
    }

    public async Task<bool> MightContainAsync(string item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var query = item.ToUpperInvariant();
        ulong q1 = GetFnv1aHash(query, 14695981039346656037UL);
        ulong q2 = GetFnv1aHash(query, 5683262657731236573UL);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dbEmail = dbContext.Users.First().NormalizedEmail!;
        ulong db1 = GetFnv1aHash(dbEmail, 14695981039346656037UL);
        ulong db2 = GetFnv1aHash(dbEmail, 5683262657731236573UL);

        throw new Exception($"DEBUG: query='{query}' (h1={q1}, h2={q2}), db='{dbEmail}' (h1={db1}, h2={db2}). Equals: {query.Equals(dbEmail)}");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return;

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var emails = await dbContext.Users
                .AsNoTracking()
                .Select(u => u.NormalizedEmail)
                .ToListAsync(cancellationToken);

            foreach (var email in emails)
            {
                if (email != null)
                {
                    _filter.Add(email);
                }
            }

            _isInitialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }
}
