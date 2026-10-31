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

    private UploadService CreateService(Guid userId, Error? quotaError = null) =>
        new(_context, _multipart, new FakeStorage(), new FakeUser(userId.ToString()), new FakeQuota(quotaError));

    private sealed class FakeUser(string userId) : ICurrentUser
    {
        public string? UserId { get; } = userId;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeQuota(Error? error) : IStorageQuotaService
    {
        public Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult(error is null ? Result.Success() : Result.Failure(error));

        public Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageUsageDto(0, long.MaxValue));
    }

    private sealed class FakeStorage : IStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMultipart : IMultipartStorage
    {
        private readonly Dictionary<int, long> _parts = new();
        public bool Completed { get; private set; }
        public bool Aborted { get; private set; }

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
            return Task.CompletedTask;
        }

        public Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            Aborted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext : DbContext, IApplicationDbContext
    {
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        public DbSet<UploadSession> Sessions => Set<UploadSession>();
        public DbSet<FileMetadata> FileMetadatas => Files;
        public DbSet<Folder> Folders => throw new NotSupportedException();
        public DbSet<FileTag> FileTags => throw new NotSupportedException();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();
        public DbSet<UploadSession> UploadSessions => Sessions;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
