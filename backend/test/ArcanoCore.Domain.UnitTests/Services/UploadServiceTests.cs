using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class UploadServiceTests
{
    private const long Chunk = UploadService.ChunkSize;
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly TestContext _context = new();
    private readonly FakeMultipart _multipart = new();
    private readonly FakeStorage _storage = new();

    [Theory]
    [InlineData("", "File.ContentTypeRequired")]
    [InlineData("   ", "File.ContentTypeRequired")]
    public async Task StartAsync_EmptyContentType_FailsWithRequiredCode(string contentType, string code)
    {
        var result = await CreateService(_ownerId).StartAsync(new UploadStartDto("a.bin", contentType, 10, null));

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public async Task StartAsync_ContentTypeOver100Chars_FailsWithTooLongCode()
    {
        var result = await CreateService(_ownerId).StartAsync(new UploadStartDto("a.bin", new string('a', 101), 10, null));

        Assert.Equal("File.ContentTypeTooLong", result.Error.Code);
    }

    [Fact]
    public async Task StartAsync_QuotaExceeded_FailsWithoutCreatingSession()
    {
        var service = CreateService(_ownerId, new Error("Storage.QuotaExceeded", "full"));

        var result = await service.StartAsync(new UploadStartDto("a.bin", "a/b", Chunk, null));

        Assert.Equal("Storage.QuotaExceeded", result.Error.Code);
        Assert.Empty(_context.Sessions);
    }

    [Fact]
    public async Task UploadPartAsync_RejectsWrongSizeAndOutOfRangePart()
    {
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", Chunk + 10, null))).Value.Id;

        var wrongSize = await service.UploadPartAsync(id, 1, new MemoryStream(new byte[10]), 10);
        var lastOk = await service.UploadPartAsync(id, 2, new MemoryStream(new byte[10]), 10);
        var outOfRange = await service.UploadPartAsync(id, 3, new MemoryStream(new byte[10]), 10);

        Assert.Equal("Upload.BadPart", wrongSize.Error.Code);
        Assert.True(lastOk.IsSuccess);
        Assert.Equal("Upload.BadPart", outOfRange.Error.Code);
    }

    [Fact]
    public async Task CompleteAsync_MissingPart_FailsThenSucceedsWhenAllUploaded()
    {
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", Chunk + 10, null))).Value.Id;
        await service.UploadPartAsync(id, 2, new MemoryStream(new byte[10]), 10);

        var early = await service.CompleteAsync(id);
        await service.UploadPartAsync(id, 1, new MemoryStream(new byte[Chunk]), Chunk);
        var done = await service.CompleteAsync(id);

        Assert.Equal("Upload.Incomplete", early.Error.Code);
        Assert.True(done.IsSuccess);
        Assert.Equal(Chunk + 10, done.Value.FileSize);
        Assert.Single(_context.Files);
        Assert.Empty(_context.Sessions);
        Assert.True(_multipart.Completed);
    }

    [Fact]
    public async Task OtherUser_CannotSeeOrCompleteSession()
    {
        var id = (await CreateService(_ownerId).StartAsync(new UploadStartDto("a.bin", "a/b", 10, null))).Value.Id;
        var other = CreateService(Guid.NewGuid());

        Assert.Equal("Upload.NotFound", (await other.GetStatusAsync(id)).Error.Code);
        Assert.Equal("Upload.NotFound", (await other.CompleteAsync(id)).Error.Code);
        Assert.Equal("Upload.NotFound", (await other.AbortAsync(id)).Error.Code);
    }

    [Fact]
    public async Task AbortAsync_RemovesSessionAndAbortsUpload()
    {
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null))).Value.Id;

        var result = await service.AbortAsync(id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_context.Sessions);
        Assert.True(_multipart.Aborted);
    }

    [Fact]
    public async Task StartAsync_SweepsOwnersStaleSessionsOnly()
    {
        var old = DateTime.UtcNow.AddDays(-2);
        _context.Sessions.AddRange(
            new UploadSession { Id = Guid.NewGuid(), OwnerId = _ownerId, CreatedAtUtc = old },
            new UploadSession { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), CreatedAtUtc = old },
            new UploadSession { Id = Guid.NewGuid(), OwnerId = _ownerId });
        await _context.SaveChangesAsync(CancellationToken.None);

        await CreateService(_ownerId).StartAsync(new UploadStartDto("a.bin", "a/b", 10, null));

        Assert.True(_multipart.Aborted);
        Assert.Equal(3, _context.Sessions.Count()); // other user's stale + own fresh + the new one
    }

    [Fact]
    public async Task StartAsync_FourthOpenSession_FailsButOtherUserCanStart()
    {
        var service = CreateService(_ownerId);
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null))).IsSuccess);
        }

        var fourth = await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null));
        var other = await CreateService(Guid.NewGuid()).StartAsync(new UploadStartDto("a.bin", "a/b", 10, null));

        Assert.Equal("Upload.TooManyOpen", fourth.Error.Code);
        Assert.True(other.IsSuccess);
    }

    [Fact]
    public async Task StartAsync_QuotaCheckIncludesOpenSessionsBytes()
    {
        var quota = new FakeQuota(null);
        var service = CreateService(_ownerId, quota: quota);
        await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null));

        await service.StartAsync(new UploadStartDto("b.bin", "a/b", 25, null));

        Assert.Equal(35, quota.LastAdditionalBytes);
    }

    [Fact]
    public async Task CompleteAsync_SavesAtRoot_WhenFolderIsDeletedAfterStart()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "folder", OwnerId = _ownerId };
        _context.Folders.Add(folder);
        await _context.SaveChangesAsync(CancellationToken.None);
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, folder.Id))).Value.Id;
        folder.IsDeleted = true;
        await _context.SaveChangesAsync(CancellationToken.None);
        await service.UploadPartAsync(id, 1, new MemoryStream(new byte[10]), 10);

        var done = await service.CompleteAsync(id);

        Assert.True(done.IsSuccess);
        Assert.Null(_context.Files.Single().FolderId);
    }

    [Fact]
    public async Task CompleteAsync_Retry_ReturnsTheSameFile()
    {
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null))).Value.Id;
        await service.UploadPartAsync(id, 1, new MemoryStream(new byte[10]), 10);
        var first = await service.CompleteAsync(id);

        var retry = await service.CompleteAsync(id);
        var other = await CreateService(Guid.NewGuid()).CompleteAsync(id);

        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value, retry.Value);
        Assert.Equal("Upload.NotFound", other.Error.Code);
    }

    [Fact]
    public async Task CompleteAsync_SecondCallDuringFirst_ReturnsTheSavedFile()
    {
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null))).Value.Id;
        await service.UploadPartAsync(id, 1, new MemoryStream(new byte[10]), 10);
        // The second call has its own context (as a separate request would), reads the session, then waits on the lock.
        var second = new UploadService(new TestContext(_context.DatabaseName), _multipart, _storage, new FakeUser(_ownerId.ToString()), new FakeQuota(null));
        Task<Result<FileResponseDto>>? secondCall = null;
        _multipart.OnComplete = () => secondCall ??= second.CompleteAsync(id);

        var first = await service.CompleteAsync(id);
        var retry = await secondCall!;

        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value, retry.Value);
        Assert.Empty(_storage.DeleteTokens);
    }

    [Fact]
    public async Task AbortAsync_S3Failure_KeepsSessionForCleanupRetry()
    {
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, null))).Value.Id;
        _multipart.AbortFails = true;

        var result = await service.AbortAsync(id);

        Assert.True(result.IsFailure);
        Assert.Single(_context.Sessions);
    }

    [Fact]
    public async Task CompleteAsync_CancelledAfterS3Complete_DeletesObjectWithoutCancelledToken_AndPropagatesCancellation()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "f", OwnerId = _ownerId };
        _context.Folders.Add(folder);
        await _context.SaveChangesAsync(CancellationToken.None);
        var service = CreateService(_ownerId);
        var id = (await service.StartAsync(new UploadStartDto("a.bin", "a/b", 10, folder.Id))).Value.Id;
        await service.UploadPartAsync(id, 1, new MemoryStream(new byte[10]), 10);
        using var cts = new CancellationTokenSource();
        _multipart.OnComplete = cts.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteAsync(id, cts.Token));

        Assert.Single(_storage.DeleteTokens);
        Assert.False(_storage.DeleteTokens[0].CanBeCanceled);
    }

    private UploadService CreateService(Guid userId, Error? quotaError = null, FakeQuota? quota = null) =>
        new(_context, _multipart, _storage, new FakeUser(userId.ToString()), quota ?? new FakeQuota(quotaError));

    private sealed class FakeUser(string userId) : ICurrentUser
    {
        public string? UserId { get; } = userId;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeQuota(Error? error) : IStorageQuotaService
    {
        public long LastAdditionalBytes { get; private set; }

        public Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default)
        {
            LastAdditionalBytes = additionalBytes;
            return Task.FromResult(error is null ? Result.Success() : Result.Failure(error));
        }

        public Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageUsageDto(0, long.MaxValue));
    }

    private sealed class FakeStorage : IStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public List<CancellationToken> DeleteTokens { get; } = new();

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeleteTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMultipart : IMultipartStorage
    {
        private readonly Dictionary<int, long> _parts = new();
        public bool Completed { get; private set; }
        public bool Aborted { get; private set; }
        public bool AbortFails { get; set; }
        public Action? OnComplete { get; set; }

        public Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult((Guid.NewGuid().ToString(), "upload-id"));

        public Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default)
        {
            _parts[partNumber] = content.Length;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, long>>(new Dictionary<int, long>(_parts));

        public Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            Completed = true;
            OnComplete?.Invoke();
            return Task.CompletedTask;
        }

        public Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            if (AbortFails) throw new IOException("boom");
            Aborted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext(string? databaseName = null) : DbContext, IApplicationDbContext
    {
        public string DatabaseName { get; } = databaseName ?? Guid.NewGuid().ToString();
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        public DbSet<UploadSession> Sessions => Set<UploadSession>();
        public DbSet<FileMetadata> FileMetadatas => Files;
        public DbSet<Folder> Folders => Set<Folder>();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();
        public DbSet<UploadSession> UploadSessions => Sessions;
        public DbSet<UserProfile> UserProfiles => throw new NotSupportedException();
        public DbSet<InviteKey> InviteKeys => throw new NotSupportedException();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(DatabaseName);
    }
}
