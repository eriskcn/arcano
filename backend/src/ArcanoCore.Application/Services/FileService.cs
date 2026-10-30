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
    private readonly IBloomFilter<Guid> _bloomFilter;
    private readonly ICurrentUser _currentUser;

    public FileService(
        IApplicationDbContext dbContext,
        IStorageService storageService,
        IBloomFilter<Guid> bloomFilter,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _bloomFilter = bloomFilter;
        _currentUser = currentUser;
    }

    public async Task<Result<FileResponseDto>> UploadFileAsync(FileUploadDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Content == null || dto.Content.Length == 0)
        {
            return Result<FileResponseDto>.Failure(new Error("File.Empty", "File content cannot be empty."));
        }

        if (!Guid.TryParse(_currentUser.UserId, out var ownerId) || ownerId == Guid.Empty)
        {
            return Result<FileResponseDto>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        try
        {
            string storageKey = await _storageService.UploadAsync(dto.Content, dto.FileName, dto.ContentType, cancellationToken);

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

            await _bloomFilter.AddAsync(fileMetadata.Id, cancellationToken);

            string url = await _storageService.GetUrlAsync(storageKey, 24, cancellationToken);

            return Result<FileResponseDto>.Success(new FileResponseDto(
                fileMetadata.Id,
                fileMetadata.FileName,
                fileMetadata.ContentType,
                fileMetadata.FileSize,
                url
            ));
        }
        catch (Exception ex)
        {
            return Result<FileResponseDto>.Failure(new Error("File.UploadFailed", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyList<FileListItemDto>>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var ownerId) || ownerId == Guid.Empty)
        {
            return Result<IReadOnlyList<FileListItemDto>>.Failure(new Error("File.Unauthorized", "A valid user ID is required."));
        }

        var files = await _dbContext.FileMetadatas
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new FileListItemDto(f.Id, f.FileName, f.ContentType, f.FileSize, f.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FileListItemDto>>.Success(files);
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _bloomFilter.MightContainAsync(id, cancellationToken))
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        var fileMetadata = await _dbContext.FileMetadatas.FindAsync(new object[] { id }, cancellationToken);
        if (fileMetadata == null || fileMetadata.IsDeleted || !IsOwner(fileMetadata))
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        try
        {
            var content = await _storageService.DownloadAsync(fileMetadata.StorageKey, cancellationToken);
            return Result<(Stream, string, string)>.Success((content, fileMetadata.ContentType, fileMetadata.FileName));
        }
        catch (Exception ex)
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.DownloadFailed", ex.Message));
        }
    }

    public async Task<Result> DeleteFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _bloomFilter.MightContainAsync(id, cancellationToken))
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        var fileMetadata = await _dbContext.FileMetadatas.FindAsync(new object[] { id }, cancellationToken);
        if (fileMetadata == null || fileMetadata.IsDeleted || !IsOwner(fileMetadata))
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        try
        {
            await _storageService.DeleteAsync(fileMetadata.StorageKey, cancellationToken);

            fileMetadata.IsDeleted = true;
            fileMetadata.LastModifiedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("File.DeleteFailed", ex.Message));
        }
    }

    private bool IsOwner(FileMetadata fileMetadata) =>
        Guid.TryParse(_currentUser.UserId, out var userId) && userId != Guid.Empty && fileMetadata.OwnerId == userId;
}
