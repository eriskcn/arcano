using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class StorageQuotaServiceTests
{
    [Fact]
    public async Task GetUsageAsync_CountsLiveAndTrashedFilesOfOwnerOnly()
    {
        var ownerId = Guid.NewGuid();
        var service = CreateService(100,
            NewFile(ownerId, 10),
            NewFile(ownerId, 20, trashed: true),
            NewFile(Guid.NewGuid(), 500));

        var usage = await service.GetUsageAsync(ownerId);

        Assert.Equal(30, usage.UsedBytes);
        Assert.Equal(100, usage.LimitBytes);
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_PassesUnderLimitAndFailsOverIt()
    {
        var ownerId = Guid.NewGuid();
        var service = CreateService(100, NewFile(ownerId, 60));

        Assert.True((await service.EnsureWithinQuotaAsync(ownerId, 40)).IsSuccess);

        var result = await service.EnsureWithinQuotaAsync(ownerId, 41);
        Assert.True(result.IsFailure);
        Assert.Equal("Storage.QuotaExceeded", result.Error.Code);
    }

    [Fact]
    public async Task GetUsageAsync_UsesOwnQuotaWhenSet_ElseGlobalLimit()
    {
        var withQuota = Guid.NewGuid();
        var withoutQuota = Guid.NewGuid();
        var service = CreateService(100, new[] { new UserProfile { UserId = withQuota, QuotaBytes = 7 }, new UserProfile { UserId = withoutQuota } });

        Assert.Equal(7, (await service.GetUsageAsync(withQuota)).LimitBytes);
        Assert.Equal(100, (await service.GetUsageAsync(withoutQuota)).LimitBytes);
        Assert.Equal(100, (await service.GetUsageAsync(Guid.NewGuid())).LimitBytes);
        Assert.True((await service.EnsureWithinQuotaAsync(withQuota, 7)).IsSuccess);
        Assert.Equal("Storage.QuotaExceeded", (await service.EnsureWithinQuotaAsync(withQuota, 8)).Error.Code);
    }

    private static FileMetadata NewFile(Guid ownerId, long size, bool trashed = false) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ownerId,
        FileSize = size,
        StorageKey = Guid.NewGuid().ToString(),
        IsDeleted = trashed,
        DeletedAtUtc = trashed ? DateTime.UtcNow : null
    };

    private static StorageQuotaService CreateService(long limit, params FileMetadata[] files) =>
        new(new TestDbContext(files, []), limit);

    private static StorageQuotaService CreateService(long limit, UserProfile[] profiles) =>
        new(new TestDbContext([], profiles), limit);

    private sealed class TestDbContext : IApplicationDbContext
    {
        public TestDbContext(FileMetadata[] files, UserProfile[] profiles)
        {
            var context = new TestContext();
            context.Files.AddRange(files);
            context.Profiles.AddRange(profiles);
            context.SaveChanges();
            FileMetadatas = context.Files;
            UserProfiles = context.Profiles;
        }

        public DbSet<FileMetadata> FileMetadatas { get; }
        public DbSet<UserProfile> UserProfiles { get; }
        public DbSet<InviteKey> InviteKeys => throw new NotSupportedException();
        public DbSet<Folder> Folders => throw new NotSupportedException();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();
        public DbSet<UploadSession> UploadSessions => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class TestContext : DbContext
    {
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        public DbSet<UserProfile> Profiles => Set<UserProfile>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileMetadata>().HasQueryFilter(file => !file.IsDeleted);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
