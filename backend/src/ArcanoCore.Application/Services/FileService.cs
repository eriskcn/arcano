using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;

namespace ArcanoCore.Application.Services;

public class FileService : IFileService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStorageService _storageService;
    private readonly IBloomFilter<Guid> _bloomFilter;

    public FileService(
        IUnitOfWork unitOfWork,
        IStorageService storageService,
        IBloomFilter<Guid> bloomFilter)
    {
        _unitOfWork = unitOfWork;
        _storageService = storageService;
        _bloomFilter = bloomFilter;
    }

    public async Task<Result<FileResponseDto>> UploadFileAsync(FileUploadDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Content == null || dto.Content.Length == 0)
        {
            return Result<FileResponseDto>.Failure(new Error("File.Empty", "File content cannot be empty."));
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
                StorageKey = storageKey
            };

            await _unitOfWork.Repository<FileMetadata>().AddAsync(fileMetadata, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await _bloomFilter.MightContainAsync(id, cancellationToken))
        {
            return Result<(Stream, string, string)>.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        var fileMetadata = await _unitOfWork.Repository<FileMetadata>().GetByIdAsync(id, cancellationToken);
        if (fileMetadata == null || fileMetadata.IsDeleted)
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

        var fileMetadata = await _unitOfWork.Repository<FileMetadata>().GetByIdAsync(id, cancellationToken);
        if (fileMetadata == null || fileMetadata.IsDeleted)
        {
            return Result.Failure(new Error("File.NotFound", "File metadata not found."));
        }

        try
        {
            await _storageService.DeleteAsync(fileMetadata.StorageKey, cancellationToken);

            fileMetadata.IsDeleted = true;
            fileMetadata.LastModifiedAtUtc = DateTime.UtcNow;

            _unitOfWork.Repository<FileMetadata>().Update(fileMetadata);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("File.DeleteFailed", ex.Message));
        }
    }
}
