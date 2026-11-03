using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

public class FileService : IFileService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly ICurrentUser _currentUser;
    private readonly IStorageQuotaService _quotaService;

    // shortcut: in-process lock, so the quota check is only race-free on a single API instance; use a DB lock when scaling out.
    internal static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UploadLocks = new();

    public FileService(
        IApplicationDbContext dbContext,
        IStorageService storageService,
        ICurrentUser currentUser,
        IStorageQuotaService quotaService)
    {
        _quotaService = quotaService;
        _dbContext = dbContext;
        _storageService = storageService;
        _currentUser = currentUser;
    }

    // Guid.Empty when the current user ID is missing or not a valid GUID.
    private Guid CurrentOwnerId => Guid.TryParse(_currentUser.UserId, out var ownerId) ? ownerId : Guid.Empty;

    public async Task<Result<FileResponseDto>> UploadFileAsync(FileUploadDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Content == null || dto.Content.Length == 0)
        {
            return Result<FileResponseDto>.Failure(new Error("File.Empty", "File content cannot be empty."));
        }

        if (dto.FileName.Length > 255)
        {
            return Result<FileResponseDto>.Failure(new Error("File.NameTooLong", "File name cannot exceed 255 characters."));
        }

        if (dto.ContentType.Length > 100)
        {
            return Result<FileResponseDto>.Failure(new Error("File.ContentTypeTooLong", "Content type cannot exceed 100 characters."));
        }

        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<FileResponseDto>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        if (dto.FolderId != null && !await IsOwnedActiveFolderAsync(dto.FolderId.Value, ownerId, cancellationToken))
        {
            return Result<FileResponseDto>.Failure(new Error("Folder.NotFound", "Folder not found."));
        }

        // One upload per user at a time, so parallel uploads cannot each pass the quota check.
        var uploadLock = UploadLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await uploadLock.WaitAsync(cancellationToken);
        string? storageKey = null;
        try
        {
            var quotaResult = await _quotaService.EnsureWithinQuotaAsync(ownerId, dto.FileSize, cancellationToken);
            if (quotaResult.IsFailure)
            {
                return Result<FileResponseDto>.Failure(quotaResult.Error);
            }

            storageKey = await _storageService.UploadAsync(dto.Content, dto.FileName, dto.ContentType, cancellationToken);

            // The folder may have been deleted during the upload; keep the bytes and fall back to the root.
            var targetFolderId = await ActiveFolderIdOrNullAsync(dto.FolderId, ownerId, cancellationToken);

            var fileMetadata = new FileMetadata
            {
                Id = Guid.NewGuid(),
                FileName = dto.FileName,
                ContentType = dto.ContentType,
                FileSize = dto.FileSize,
                StorageKey = storageKey,
                OwnerId = ownerId,
                FolderId = targetFolderId
            };

            await _dbContext.FileMetadatas.AddAsync(fileMetadata, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<FileResponseDto>.Success(new FileResponseDto(
                fileMetadata.Id,
                fileMetadata.FileName,
                fileMetadata.ContentType,
                fileMetadata.FileSize
            ));
        }
        catch (Exception)
        {
            if (storageKey != null)
            {
                try
                {
                    await _storageService.DeleteAsync(storageKey, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Best effort: the original upload failure is returned below.
                }
            }

            return Result<FileResponseDto>.Failure(new Error("File.UploadFailed", "Upload failed. Please try again."));
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<Result<IReadOnlyList<FileListItemDto>>> ListFilesAsync(int skip, int take, Guid? folderId = null, string? view = null, string? query = null, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<IReadOnlyList<FileListItemDto>>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        var normalizedView = string.IsNullOrWhiteSpace(view) ? "all" : view.Trim().ToLowerInvariant();
        if (normalizedView is not ("all" or "starred" or "recent"))
        {
            return Result<IReadOnlyList<FileListItemDto>>.Failure(new Error("File.InvalidView", "Unknown view."));
        }

        var live = _dbContext.FileMetadatas
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted);

        IOrderedQueryable<FileMetadata> ordered;
        if (!string.IsNullOrEmpty(query))
        {
            var q = query.ToLower();
            ordered = live.Where(f => f.FileName.ToLower().Contains(q))
                .OrderByDescending(f => f.CreatedAtUtc).ThenBy(f => f.Id);
        }
        else if (normalizedView == "recent")
        {
            ordered = live.OrderByDescending(f => f.LastModifiedAtUtc ?? f.CreatedAtUtc).ThenBy(f => f.Id);
        }
        else if (normalizedView == "starred")
        {
            ordered = live.Where(f => f.IsStarred).OrderByDescending(f => f.CreatedAtUtc).ThenBy(f => f.Id);
        }
        else
        {
            // A null folderId lists the root (FolderId is null).
            ordered = live.Where(f => f.FolderId == folderId).OrderByDescending(f => f.CreatedAtUtc).ThenBy(f => f.Id);
        }

        var files = await ordered
            .Skip(skip)
            .Take(take)
            .Select(f => new FileListItemDto(f.Id, f.FileName, f.ContentType, f.FileSize, f.CreatedAtUtc, f.IsStarred, f.LastModifiedAtUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FileListItemDto>>.Success(files);
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fileMetadata = await FindOwnedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        try
        {
            var content = await _storageService.DownloadAsync(fileMetadata.StorageKey, cancellationToken);
            return Result<(Stream, string, string)>.Success((content, fileMetadata.ContentType, fileMetadata.FileName));
        }
        catch (Exception)
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.DownloadFailed", "Download failed. Please try again."));
        }
    }

    public async Task<Result> DeleteFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fileMetadata = await FindOwnedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        try
        {
            var now = DateTime.UtcNow;
            fileMetadata.IsDeleted = true;
            fileMetadata.DeletedAtUtc = now;
            fileMetadata.LastModifiedAtUtc = now;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            return Result.Failure(new Error("File.DeleteFailed", "Delete failed. Please try again."));
        }
    }

    public async Task<Result> RenameFileAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > 255)
        {
            return Result.Failure(new Error("File.InvalidName", "Name must be 1 to 255 characters."));
        }

        var fileMetadata = await FindOwnedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        fileMetadata.FileName = trimmed;
        fileMetadata.LastModifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> SetStarredAsync(Guid id, bool isStarred, CancellationToken cancellationToken = default)
    {
        var fileMetadata = await FindOwnedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        fileMetadata.IsStarred = isStarred;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<FileListItemDto>>> ListTrashAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<IReadOnlyList<FileListItemDto>>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        var files = await _dbContext.FileMetadatas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && f.IsDeleted)
            .OrderByDescending(f => f.DeletedAtUtc)
            .ThenBy(f => f.Id)
            .Skip(skip)
            .Take(take)
            .Select(f => new FileListItemDto(f.Id, f.FileName, f.ContentType, f.FileSize, f.CreatedAtUtc, f.IsStarred, f.LastModifiedAtUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FileListItemDto>>.Success(files);
    }

    public async Task<Result> RestoreFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fileMetadata = await FindOwnedTrashedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        if (fileMetadata.FolderId != null)
        {
            var folderId = fileMetadata.FolderId.Value;
            var folderIsLive = await _dbContext.Folders
                .IgnoreQueryFilters()
                .AnyAsync(f => f.Id == folderId && !f.IsDeleted, cancellationToken);
            if (!folderIsLive)
            {
                fileMetadata.FolderId = null;
            }
        }

        fileMetadata.IsDeleted = false;
        fileMetadata.DeletedAtUtc = null;
        fileMetadata.LastModifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> PermanentlyDeleteFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fileMetadata = await FindOwnedTrashedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        return await PurgeAsync(fileMetadata, cancellationToken);
    }

    public async Task<Result> EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        var trashed = await _dbContext.FileMetadatas
            .IgnoreQueryFilters()
            .Where(f => f.OwnerId == ownerId && f.IsDeleted)
            .ToListAsync(cancellationToken);

        var failed = false;
        foreach (var file in trashed)
        {
            failed |= (await PurgeAsync(file, cancellationToken)).IsFailure;
        }

        return failed
            ? Result.Failure(new Error("File.DeleteFailed", "Delete failed. Please try again."))
            : Result.Success();
    }

    private async Task<Result> PurgeAsync(FileMetadata file, CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.FileMetadatas.Remove(file);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // shortcut: a failed save leaves the entity tracked as Deleted (no Entry API on IApplicationDbContext), so a retry in the same scope fails again; add detach support if that matters.
            return Result.Failure(new Error("File.DeleteFailed", "Delete failed. Please try again."));
        }

        try
        {
            await _storageService.DeleteAsync(file.StorageKey, CancellationToken.None);
        }
        catch (Exception)
        {
            // The row is already gone, so a storage failure only orphans the object; do not fail the user.
        }

        return Result.Success();
    }

    public async Task<Result> MoveFileAsync(Guid id, Guid? folderId, CancellationToken cancellationToken = default)
    {
        var fileMetadata = await FindOwnedFileAsync(id, cancellationToken);
        if (fileMetadata == null)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        if (folderId != null && !await IsOwnedActiveFolderAsync(folderId.Value, CurrentOwnerId, cancellationToken))
        {
            return Result.Failure(new Error("Folder.NotFound", "Folder not found."));
        }

        fileMetadata.FolderId = folderId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<FileResponseDto>> CopyFileAsync(Guid id, Guid? folderId, CancellationToken cancellationToken = default)
    {
        var source = await FindOwnedFileAsync(id, cancellationToken);
        if (source == null)
        {
            return Result<FileResponseDto>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        var ownerId = CurrentOwnerId;
        if (folderId != null && !await IsOwnedActiveFolderAsync(folderId.Value, ownerId, cancellationToken))
        {
            return Result<FileResponseDto>.Failure(new Error("Folder.NotFound", "Folder not found."));
        }

        // Same per-user lock as upload, so a copy cannot race another upload past the quota check.
        var uploadLock = UploadLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await uploadLock.WaitAsync(cancellationToken);
        string? storageKey = null;
        try
        {
            var quotaResult = await _quotaService.EnsureWithinQuotaAsync(ownerId, source.FileSize, cancellationToken);
            if (quotaResult.IsFailure)
            {
                return Result<FileResponseDto>.Failure(quotaResult.Error);
            }

            // shortcut: streams the object through the API; use S3 CopyObject (5 GB limit) if copies get heavy.
            await using (var content = await _storageService.DownloadAsync(source.StorageKey, cancellationToken))
            {
                storageKey = await _storageService.UploadAsync(content, source.FileName, source.ContentType, cancellationToken);
            }

            var targetFolderId = await ActiveFolderIdOrNullAsync(folderId, ownerId, cancellationToken);

            var copy = new FileMetadata
            {
                Id = Guid.NewGuid(),
                FileName = $"Copy of {source.FileName}"[..Math.Min(255, source.FileName.Length + 8)],
                ContentType = source.ContentType,
                FileSize = source.FileSize,
                StorageKey = storageKey,
                OwnerId = ownerId,
                FolderId = targetFolderId
            };

            await _dbContext.FileMetadatas.AddAsync(copy, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<FileResponseDto>.Success(new FileResponseDto(copy.Id, copy.FileName, copy.ContentType, copy.FileSize));
        }
        catch (Exception)
        {
            if (storageKey != null)
            {
                try
                {
                    await _storageService.DeleteAsync(storageKey, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Best effort: the original copy failure is returned below.
                }
            }

            return Result<FileResponseDto>.Failure(new Error("File.CopyFailed", "Copy failed. Please try again."));
        }
        finally
        {
            uploadLock.Release();
        }
    }

    private Task<bool> IsOwnedActiveFolderAsync(Guid folderId, Guid ownerId, CancellationToken cancellationToken) =>
        _dbContext.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == ownerId && !f.IsDeleted, cancellationToken);

    private async Task<Guid?> ActiveFolderIdOrNullAsync(Guid? folderId, Guid ownerId, CancellationToken cancellationToken) =>
        folderId != null && await IsOwnedActiveFolderAsync(folderId.Value, ownerId, cancellationToken) ? folderId : null;

    private async Task<FileMetadata?> FindOwnedFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return null;
        }

        var fileMetadata = await _dbContext.FileMetadatas.FindAsync(new object[] { id }, cancellationToken);
        return fileMetadata != null && !fileMetadata.IsDeleted && fileMetadata.OwnerId == ownerId
            ? fileMetadata
            : null;
    }

    private async Task<FileMetadata?> FindOwnedTrashedFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return null;
        }

        // Find/FindAsync applies the "not deleted" query filter on the real provider, so trashed rows need an explicit query.
        return await _dbContext.FileMetadatas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId && f.IsDeleted, cancellationToken);
    }
}
