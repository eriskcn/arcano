using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

public class ShareService : IShareService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly ICurrentUser _currentUser;
    private readonly IUserDirectory _users;

    public ShareService(IApplicationDbContext dbContext, IStorageService storageService, ICurrentUser currentUser, IUserDirectory users)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _currentUser = currentUser;
        _users = users;
    }

    private Guid CurrentOwnerId => Guid.TryParse(_currentUser.UserId, out var ownerId) ? ownerId : Guid.Empty;

    public async Task<Result<ShareLinkDto>> CreateLinkAsync(ShareLinkRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (!await OwnsFileAsync(dto.FileId, cancellationToken))
        {
            return Result<ShareLinkDto>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        var expiresAtUtc = dto.ExpiresAtUtc is { } e
            ? e.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : e.ToUniversalTime()
            : (DateTime?)null;
        if (expiresAtUtc != null && expiresAtUtc <= DateTime.UtcNow)
        {
            return Result<ShareLinkDto>.Failure(new Error("Share.InvalidExpiry", "Expiry must be in the future."));
        }

        var link = new SharedLink
        {
            Id = Guid.NewGuid(),
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            FileMetadataId = dto.FileId,
            CreatedById = CurrentOwnerId,
            ExpiresAtUtc = expiresAtUtc
        };
        _dbContext.SharedLinks.Add(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<ShareLinkDto>.Success(ToDto(link));
    }

    public async Task<Result<IReadOnlyList<ShareLinkDto>>> ListLinksAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (!await OwnsFileAsync(fileId, cancellationToken))
        {
            return Result<IReadOnlyList<ShareLinkDto>>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        var now = DateTime.UtcNow;
        var links = await _dbContext.SharedLinks
            .Where(l => l.FileMetadataId == fileId && l.IsActive && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > now))
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ShareLinkDto>>.Success(links.Select(ToDto).ToList());
    }

    public async Task<Result> RevokeLinkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        var link = ownerId == Guid.Empty
            ? null
            : await _dbContext.SharedLinks.FirstOrDefaultAsync(l => l.Id == id && l.CreatedById == ownerId && l.IsActive, cancellationToken);
        if (link == null)
        {
            return Result.Failure(new Error("Share.NotFound", "Share link not found."));
        }

        link.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<SharedFileInfoDto>> GetSharedFileInfoAsync(string token, CancellationToken cancellationToken = default)
    {
        var file = await FindSharedFileAsync(token, cancellationToken);
        return file == null
            ? Result<SharedFileInfoDto>.Failure(new Error("Share.NotFound", "Share link not found."))
            : Result<SharedFileInfoDto>.Success(new SharedFileInfoDto(file.FileName, file.ContentType, file.FileSize));
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> OpenSharedFileAsync(string token, CancellationToken cancellationToken = default)
    {
        var file = await FindSharedFileAsync(token, cancellationToken);
        if (file == null)
        {
            return Result<(Stream, string, string)>.Failure(new Error("Share.NotFound", "Share link not found."));
        }

        try
        {
            var content = await _storageService.DownloadAsync(file.StorageKey, cancellationToken);
            return Result<(Stream, string, string)>.Success((content, file.ContentType, file.FileName));
        }
        catch (Exception)
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.DownloadFailed", "Download failed. Please try again."));
        }
    }

    private async Task<FileMetadata?> FindSharedFileAsync(string token, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var fileId = await _dbContext.SharedLinks
            .Where(l => l.Token == token && l.IsActive && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > now))
            .Select(l => l.FileMetadataId)
            .FirstOrDefaultAsync(cancellationToken);
        if (fileId == null)
        {
            return null;
        }

        // The query filter hides trashed files, so a link to a trashed file stops working.
        var file = await _dbContext.FileMetadatas.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file == null)
        {
            return null;
        }

        // Checked per request, so a link stops while the owner is disabled and works again after enable.
        var owners = await _users.GetByIdsAsync([file.OwnerId], cancellationToken);
        return owners.TryGetValue(file.OwnerId, out var owner) && !owner.IsDisabled ? file : null;
    }

    private async Task<bool> OwnsFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var ownerId = CurrentOwnerId;
        return ownerId != Guid.Empty &&
            await _dbContext.FileMetadatas.AnyAsync(f => f.Id == fileId && f.OwnerId == ownerId, cancellationToken);
    }

    private static ShareLinkDto ToDto(SharedLink link) =>
        new(link.Id, link.FileMetadataId!.Value, link.Token, link.ExpiresAtUtc, link.CreatedAtUtc);
}
