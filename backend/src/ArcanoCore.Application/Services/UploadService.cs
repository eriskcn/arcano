using System;
using System.Collections.Concurrent;
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

// Chunked uploads on top of S3 multipart. shortcut: abandoned sessions are swept only when their owner starts another upload; add a background sweep if owners never return.
public class UploadService : IUploadService
{
    public const long ChunkSize = 8 * 1024 * 1024; // above S3's 5 MiB minimum part size
    private const int MaxParts = 10000; // S3 limit
    private const int MaxOpenSessions = 3;
    // Held only while a session is started, so concurrent starts cannot slip past the session cap.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> StartLocks = new();
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _dbContext;
    private readonly IMultipartStorage _multipart;
    private readonly IStorageService _storageService;
    private readonly ICurrentUser _currentUser;
    private readonly IStorageQuotaService _quotaService;

    public UploadService(
        IApplicationDbContext dbContext,
        IMultipartStorage multipart,
        IStorageService storageService,
        ICurrentUser currentUser,
        IStorageQuotaService quotaService)
    {
        _dbContext = dbContext;
        _multipart = multipart;
        _storageService = storageService;
        _currentUser = currentUser;
        _quotaService = quotaService;
    }

    private Guid CurrentOwnerId => Guid.TryParse(_currentUser.UserId, out var ownerId) ? ownerId : Guid.Empty;

    public async Task<Result<UploadStartedDto>> StartAsync(UploadStartDto dto, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Fail<UploadStartedDto>("File.Unauthorized", "A valid user ID is required.");
        }

        if (dto.FileSize <= 0)
        {
            return Fail<UploadStartedDto>("File.Empty", "File content cannot be empty.");
        }

        if (dto.FileSize > MaxParts * ChunkSize)
        {
            return Fail<UploadStartedDto>("File.TooLarge", "File is too large.");
        }

        if (string.IsNullOrWhiteSpace(dto.FileName) || dto.FileName.Length > 255)
        {
            return Fail<UploadStartedDto>("File.NameTooLong", "File name must be 1 to 255 characters.");
        }

        if (string.IsNullOrWhiteSpace(dto.ContentType))
        {
            return Fail<UploadStartedDto>("File.ContentTypeRequired", "Content type is required.");
        }

        if (dto.ContentType.Length > 100)
        {
            return Fail<UploadStartedDto>("File.ContentTypeTooLong", "Content type cannot exceed 100 characters.");
        }

        if (dto.FolderId != null &&
            !await _dbContext.Folders.AnyAsync(f => f.Id == dto.FolderId && f.OwnerId == ownerId && !f.IsDeleted, cancellationToken))
        {
            return Fail<UploadStartedDto>("Folder.NotFound", "Folder not found.");
        }

        var startLock = StartLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await startLock.WaitAsync(cancellationToken);
        try
        {
            await SweepAbandonedAsync(ownerId, cancellationToken);
            var open = await _dbContext.UploadSessions.Where(s => s.OwnerId == ownerId).ToListAsync(cancellationToken);
            if (open.Count >= MaxOpenSessions)
            {
                return Fail<UploadStartedDto>("Upload.TooManyOpen", "Too many uploads in progress. Finish or cancel one first.");
            }

            // Early check so an over-quota upload is refused before any data is sent; open sessions count as already reserved. Complete checks again.
            var quotaResult = await _quotaService.EnsureWithinQuotaAsync(ownerId, open.Sum(s => s.FileSize) + dto.FileSize, cancellationToken);
            if (quotaResult.IsFailure)
            {
                return Result<UploadStartedDto>.Failure(quotaResult.Error);
            }

            var (key, uploadId) = await _multipart.InitiateAsync(dto.ContentType, cancellationToken);
            var session = new UploadSession
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                FileName = dto.FileName,
                ContentType = dto.ContentType,
                FileSize = dto.FileSize,
                FolderId = dto.FolderId,
                StorageKey = key,
                StorageUploadId = uploadId
            };
            await _dbContext.UploadSessions.AddAsync(session, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result<UploadStartedDto>.Success(new UploadStartedDto(session.Id, ChunkSize));
        }
        catch (Exception)
        {
            return Fail<UploadStartedDto>("File.UploadFailed", "Upload failed. Please try again.");
        }
        finally
        {
            startLock.Release();
        }
    }

    public async Task<Result<UploadStatusDto>> GetStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await FindOwnedSessionAsync(id, cancellationToken);
        if (session == null)
        {
            return Fail<UploadStatusDto>("Upload.NotFound", "Upload not found.");
        }

        try
        {
            var parts = await _multipart.ListPartsAsync(session.StorageKey, session.StorageUploadId, cancellationToken);
            return Result<UploadStatusDto>.Success(new UploadStatusDto(session.FileSize, ChunkSize, parts.Keys.Order().ToList()));
        }
        catch (Exception)
        {
            return Fail<UploadStatusDto>("File.UploadFailed", "Upload failed. Please try again.");
        }
    }

    public async Task<Result> UploadPartAsync(Guid id, int partNumber, Stream content, long length, CancellationToken cancellationToken = default)
    {
        var session = await FindOwnedSessionAsync(id, cancellationToken);
        if (session == null)
        {
            return Result.Failure(new Error("Upload.NotFound", "Upload not found."));
        }

        // Every part but the last is exactly ChunkSize, so the size is known up front and a client cannot send more.
        if (partNumber < 1 || partNumber > PartCount(session.FileSize) || length != ExpectedPartSize(session.FileSize, partNumber))
        {
            return Result.Failure(new Error("Upload.BadPart", "Unexpected part number or size."));
        }

        try
        {
            // Buffered (at most ChunkSize) because the S3 client needs a seekable stream with a known length.
            using var buffer = new MemoryStream((int)length);
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length != length)
            {
                return Result.Failure(new Error("Upload.BadPart", "Unexpected part number or size."));
            }

            buffer.Position = 0;
            await _multipart.UploadPartAsync(session.StorageKey, session.StorageUploadId, partNumber, buffer, cancellationToken);
            return Result.Success();
        }
        catch (Exception)
        {
            return Result.Failure(new Error("File.UploadFailed", "Upload failed. Please try again."));
        }
    }

    public async Task<Result<FileResponseDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await FindOwnedSessionAsync(id, cancellationToken);
        if (session == null)
        {
            return Fail<FileResponseDto>("Upload.NotFound", "Upload not found.");
        }

        // Same per-user lock as single-request uploads, so the quota check and the save cannot interleave.
        var uploadLock = FileService.UploadLocks.GetOrAdd(session.OwnerId, _ => new SemaphoreSlim(1, 1));
        await uploadLock.WaitAsync(cancellationToken);
        try
        {
            var parts = await _multipart.ListPartsAsync(session.StorageKey, session.StorageUploadId, cancellationToken);
            var complete = parts.Count == PartCount(session.FileSize) &&
                Enumerable.Range(1, parts.Count).All(n => parts.TryGetValue(n, out var size) && size == ExpectedPartSize(session.FileSize, n));
            if (!complete)
            {
                return Fail<FileResponseDto>("Upload.Incomplete", "Not all parts have been uploaded.");
            }

            var quotaResult = await _quotaService.EnsureWithinQuotaAsync(session.OwnerId, session.FileSize, cancellationToken);
            if (quotaResult.IsFailure)
            {
                return Result<FileResponseDto>.Failure(quotaResult.Error);
            }

            await _multipart.CompleteAsync(session.StorageKey, session.StorageUploadId, cancellationToken);
            try
            {
                // The folder may have been deleted during the upload; keep the bytes and fall back to the root.
                var folderId = session.FolderId != null &&
                    await _dbContext.Folders.AnyAsync(f => f.Id == session.FolderId && f.OwnerId == session.OwnerId && !f.IsDeleted, cancellationToken)
                        ? session.FolderId
                        : null;
                var file = new FileMetadata
                {
                    Id = Guid.NewGuid(),
                    FileName = session.FileName,
                    ContentType = session.ContentType,
                    FileSize = session.FileSize,
                    StorageKey = session.StorageKey,
                    OwnerId = session.OwnerId,
                    FolderId = folderId
                };
                await _dbContext.FileMetadatas.AddAsync(file, cancellationToken);
                _dbContext.UploadSessions.Remove(session);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result<FileResponseDto>.Success(new FileResponseDto(file.Id, file.FileName, file.ContentType, file.FileSize));
            }
            catch (Exception)
            {
                try
                {
                    await _storageService.DeleteAsync(session.StorageKey, cancellationToken);
                }
                catch (Exception)
                {
                    // Best effort: the original failure is returned below.
                }
                throw;
            }
        }
        catch (Exception)
        {
            return Fail<FileResponseDto>("File.UploadFailed", "Upload failed. Please try again.");
        }
        finally
        {
            uploadLock.Release();
        }
    }

    public async Task<Result> AbortAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await FindOwnedSessionAsync(id, cancellationToken);
        if (session == null)
        {
            return Result.Failure(new Error("Upload.NotFound", "Upload not found."));
        }

        try
        {
            await _multipart.AbortAsync(session.StorageKey, session.StorageUploadId, cancellationToken);
        }
        catch (Exception)
        {
            // The session row is removed either way; a leftover multipart upload is the shortcut noted above.
        }

        _dbContext.UploadSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task SweepAbandonedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - SessionLifetime;
        var stale = await _dbContext.UploadSessions
            .Where(s => s.OwnerId == ownerId && s.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);
        foreach (var session in stale)
        {
            try
            {
                await _multipart.AbortAsync(session.StorageKey, session.StorageUploadId, cancellationToken);
            }
            catch (Exception)
            {
                // Best effort: the row is removed either way.
            }

            _dbContext.UploadSessions.Remove(session);
        }

        if (stale.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<UploadSession?> FindOwnedSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentOwnerId;
        return ownerId == Guid.Empty
            ? null
            : await _dbContext.UploadSessions.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == ownerId, cancellationToken);
    }

    private static int PartCount(long fileSize) => (int)((fileSize + ChunkSize - 1) / ChunkSize);

    private static long ExpectedPartSize(long fileSize, int partNumber) =>
        partNumber == PartCount(fileSize) ? fileSize - (partNumber - 1) * ChunkSize : ChunkSize;

    private static Result<T> Fail<T>(string code, string message) => Result<T>.Failure(new Error(code, message));
}
