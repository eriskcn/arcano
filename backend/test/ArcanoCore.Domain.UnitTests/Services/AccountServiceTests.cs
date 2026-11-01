using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class AccountServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherId = Guid.NewGuid();
    private readonly TestContext _context = new();
    private readonly FakeStorage _storage = new();

    [Fact]
    public async Task DeleteUserDataAsync_RemovesOnlyOwnedData_AndStoredObjects()
    {
        var folder = new Folder { Id = Guid.NewGuid(), Name = "f", OwnerId = _userId };
        var live = new FileMetadata { Id = Guid.NewGuid(), OwnerId = _userId, StorageKey = "live", FolderId = folder.Id };
        var trashed = new FileMetadata { Id = Guid.NewGuid(), OwnerId = _userId, StorageKey = "trash", IsDeleted = true };
        var foreign = new FileMetadata { Id = Guid.NewGuid(), OwnerId = _otherId, StorageKey = "foreign" };
        _context.AddRange(folder, live, trashed, foreign,
            new Folder { Id = Guid.NewGuid(), Name = "x", OwnerId = _otherId },
            new SharedLink { Id = Guid.NewGuid(), Token = "a", CreatedById = _userId, FileMetadataId = live.Id },
            new SharedLink { Id = Guid.NewGuid(), Token = "b", CreatedById = _otherId, FileMetadataId = foreign.Id },
            new UploadSession { Id = Guid.NewGuid(), OwnerId = _userId, StorageKey = "part", StorageUploadId = "u1" });
        await _context.SaveChangesAsync(default);

        await new AccountService(_context, _storage, _storage).DeleteUserDataAsync(_userId);

        Assert.Equal(["foreign"], await _context.FileMetadatas.IgnoreQueryFilters().Select(f => f.StorageKey).ToListAsync());
        Assert.Equal([_otherId], await _context.Folders.Select(f => f.OwnerId).ToListAsync());
        Assert.Equal(["b"], await _context.SharedLinks.Select(l => l.Token).ToListAsync());
        Assert.Empty(_context.UploadSessions);
        Assert.Equal(["live", "trash"], _storage.Deleted.Order().ToList());
        Assert.Equal(["part"], _storage.Aborted);
    }

    [Fact]
    public async Task DeleteUserDataAsync_StorageFailure_IsIgnored()
    {
        _context.Add(new FileMetadata { Id = Guid.NewGuid(), OwnerId = _userId, StorageKey = "k" });
        await _context.SaveChangesAsync(default);
        _storage.Fail = true;

        await new AccountService(_context, _storage, _storage).DeleteUserDataAsync(_userId);

        Assert.Empty(_context.FileMetadatas.IgnoreQueryFilters());
    }

    private sealed class FakeStorage : IStorageService, IMultipartStorage
    {
        public List<string> Deleted { get; } = [];
        public List<string> Aborted { get; } = [];
        public bool Fail { get; set; }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException();
            Deleted.Add(key);
            return Task.CompletedTask;
        }

        public Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            Aborted.Add(key);
            return Task.CompletedTask;
        }

        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestContext : DbContext, IApplicationDbContext
    {
        public DbSet<FileMetadata> FileMetadatas => Set<FileMetadata>();
        public DbSet<Folder> Folders => Set<Folder>();
        public DbSet<SharedLink> SharedLinks => Set<SharedLink>();
        public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
        public DbSet<UserProfile> UserProfiles => throw new NotSupportedException();
        public DbSet<InviteKey> InviteKeys => throw new NotSupportedException();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileMetadata>().HasQueryFilter(file => !file.IsDeleted);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
