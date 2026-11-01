using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class CleanupServiceTests
{
    private readonly TestContext _context = new();
    private readonly FakeStorage _storage = new();
    private readonly FakeMultipart _multipart = new();

    private Task RunAsync() =>
        CleanupService.RunOnceAsync(_context, _storage, _multipart, NullLogger.Instance, CancellationToken.None);

    private static UploadSession Session(string key, TimeSpan age) =>
        new() { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), StorageKey = key, StorageUploadId = "u", CreatedAtUtc = DateTime.UtcNow - age };

    private static FileMetadata File(string key, bool deleted, TimeSpan trashedAge) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            FileName = key,
            StorageKey = key,
            CreatedAtUtc = DateTime.UtcNow - TimeSpan.FromDays(90),
            IsDeleted = deleted,
            DeletedAtUtc = deleted ? DateTime.UtcNow - trashedAge : null
        };

    [Fact]
    public async Task RunOnce_AbortsAndRemovesStaleSessionsOfAllUsers_KeepsFreshOne()
    {
        _context.Sessions.AddRange(Session("old1", TimeSpan.FromHours(25)), Session("old2", TimeSpan.FromDays(3)), Session("fresh", TimeSpan.FromHours(1)));
        await _context.SaveChangesAsync();

        await RunAsync();

        Assert.Equal(new[] { "fresh" }, _context.Sessions.Select(s => s.StorageKey).ToArray());
        Assert.Equal(new[] { "old1", "old2" }, _multipart.Aborted.Order().ToArray());
    }

    [Fact]
    public async Task RunOnce_PurgesTrashOlderThan30DaysOfAllUsers_KeepsRecentAndLive()
    {
        _context.Files.AddRange(
            File("old1", true, TimeSpan.FromDays(31)),
            File("old2", true, TimeSpan.FromDays(60)),
            File("recent", true, TimeSpan.FromDays(5)),
            File("live", false, TimeSpan.Zero));
        await _context.SaveChangesAsync();

        await RunAsync();

        Assert.Equal(new[] { "live", "recent" }, _context.Files.IgnoreQueryFilters().Select(f => f.StorageKey).Order().ToArray());
        Assert.Equal(new[] { "old1", "old2" }, _storage.Deleted.Order().ToArray());
    }

    private static Folder Folder(string name, Guid? parentId, TimeSpan deletedAge) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Name = name,
            ParentId = parentId,
            IsDeleted = true,
            CreatedAtUtc = DateTime.UtcNow - TimeSpan.FromDays(90),
            LastModifiedAtUtc = DateTime.UtcNow - deletedAge
        };

    [Fact]
    public async Task RunOnce_PurgesOldDeletedFolders_KeepsRecentAndThoseStillReferenced()
    {
        var oldParent = Folder("oldParent", null, TimeSpan.FromDays(40));
        var oldChild = Folder("oldChild", oldParent.Id, TimeSpan.FromDays(40));
        var withFile = Folder("withFile", null, TimeSpan.FromDays(40));
        var withRecentFile = File("recentFile", true, TimeSpan.FromDays(1));
        withRecentFile.FolderId = withFile.Id;
        _context.Folders.AddRange(oldParent, oldChild, withFile, Folder("recent", null, TimeSpan.FromDays(2)));
        _context.Files.Add(withRecentFile);
        await _context.SaveChangesAsync();

        await RunAsync();

        Assert.Equal(new[] { "recent", "withFile" }, _context.Folders.IgnoreQueryFilters().Select(f => f.Name).Order().ToArray());
    }

    [Fact]
    public async Task RunOnce_StorageDeleteFailure_StillRemovesRowsAndContinues()
    {
        _storage.FailKey = "old1";
        _context.Files.AddRange(File("old1", true, TimeSpan.FromDays(40)), File("old2", true, TimeSpan.FromDays(40)));
        await _context.SaveChangesAsync();

        await RunAsync();

        Assert.Empty(_context.Files);
        Assert.Equal(new[] { "old2" }, _storage.Deleted.ToArray());
    }

    private sealed class FakeStorage : IStorageService
    {
        public List<string> Deleted { get; } = new();
        public string? FailKey { get; set; }

        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key == FailKey) throw new IOException("boom");
            Deleted.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMultipart : IMultipartStorage
    {
        public List<string> Aborted { get; } = new();

        public Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            Aborted.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class TestContext : DbContext, IApplicationDbContext
    {
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        public DbSet<UploadSession> Sessions => Set<UploadSession>();
        public DbSet<FileMetadata> FileMetadatas => Files;
        public DbSet<Folder> Folders => Set<Folder>();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();
        public DbSet<UploadSession> UploadSessions => Sessions;
        public DbSet<UserProfile> UserProfiles => throw new NotSupportedException();
        public DbSet<InviteKey> InviteKeys => throw new NotSupportedException();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
