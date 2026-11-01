using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

public class InviteKeyService : IInviteKeyService
{
    private readonly IApplicationDbContext _dbContext;

    public InviteKeyService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // SHA-256 is enough because the key is 256 random bits (nothing to brute-force).
    public static string Hash(string plainKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainKey)));

    public async Task<(InviteKey Key, string Plain)> CreateAsync(InviteKeyType type, long? quotaBytes, Guid? targetUserId, int expiresInDays, Guid createdById, CancellationToken cancellationToken = default)
    {
        var plain = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var key = new InviteKey
        {
            Id = Guid.NewGuid(),
            KeyHash = Hash(plain),
            Type = type,
            QuotaBytes = type == InviteKeyType.Join ? quotaBytes : null,
            TargetUserId = type == InviteKeyType.Reset ? targetUserId : null,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(expiresInDays),
            CreatedById = createdById
        };
        _dbContext.InviteKeys.Add(key);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (key, plain);
    }

    public async Task<Result<InviteKey>> ConsumeAsync(string? plainKey, InviteKeyType type, CancellationToken cancellationToken = default)
    {
        var invalid = Result<InviteKey>.Failure(new Error("Auth.InviteInvalid", "The key is invalid, expired or already used."));
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            return invalid;
        }

        var hash = Hash(plainKey.Trim());
        var now = DateTime.UtcNow;
        var key = await _dbContext.InviteKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, cancellationToken);
        if (key == null || key.Type != type || key.UsedAtUtc != null || key.ExpiresAtUtc <= now)
        {
            return invalid;
        }

        key.UsedAtUtc = now;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return invalid; // another request used it first
        }
        return key;
    }

    public async Task ReleaseAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.InviteKeys.FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (key != null)
        {
            key.UsedAtUtc = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<InviteKey>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbContext.InviteKeys
            .Where(k => k.UsedAtUtc == null && k.ExpiresAtUtc > now)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RevokeAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await _dbContext.InviteKeys.FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (key == null)
        {
            return false;
        }
        _dbContext.InviteKeys.Remove(key);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
