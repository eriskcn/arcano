using System;
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

    public FileService(
        IApplicationDbContext dbContext,
        IStorageService storageService,
        ICurrentUser currentUser)
    {
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

        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<FileResponseDto>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        string? storageKey = null;
        try
        {
            storageKey = await _storageService.UploadAsync(dto.Content, dto.FileName, dto.ContentType, cancellationToken);

            var fileMetadata = new FileMetadata
            {
                Id = Guid.NewGuid(),
                FileName = dto.FileName,
                ContentType = dto.ContentType,
                FileSize = dto.FileSize,
                StorageKey = storageKey,
                OwnerId = ownerId
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
                    await _storageService.DeleteAsync(storageKey, cancellationToken);
                }
                catch (Exception)
                {
                    // Best effort: the original upload failure is returned below.
                }
            }

            return Result<FileResponseDto>.Failure(new Error("File.UploadFailed", "Upload failed. Please try again."));
        }
    }

    public async Task<Result<IReadOnlyList<FileListItemDto>>> ListFilesAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<IReadOnlyList<FileListItemDto>>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        var files = await _dbContext.FileMetadatas
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(f => new FileListItemDto(f.Id, f.FileName, f.ContentType, f.FileSize, f.CreatedAtUtc))
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
            fileMetadata.IsDeleted = true;
            fileMetadata.LastModifiedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _storageService.DeleteAsync(fileMetadata.StorageKey, cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            return Result.Failure(new Error("File.DeleteFailed", "Delete failed. Please try again."));
        }
    }

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
}
