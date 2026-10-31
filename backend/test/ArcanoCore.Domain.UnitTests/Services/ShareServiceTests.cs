using System.Text;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class ShareServiceTests
{
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly TestContext _context = new();

    [Fact]
    public async Task CreateLinkAsync_OtherUsersFile_ReturnsNotFound()
    {
        var file = AddFile(Guid.NewGuid());

        var result = await CreateService(_ownerId).CreateLinkAsync(new ShareLinkRequestDto(file.Id, null));

        Assert.Equal("File.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task CreateLinkAsync_UnspecifiedKindExpiry_IsTreatedAsUtc()
    {
        var file = AddFile(_ownerId);
        var expiry = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(1), DateTimeKind.Unspecified);

        var result = await CreateService(_ownerId).CreateLinkAsync(new ShareLinkRequestDto(file.Id, expiry));

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeKind.Utc, result.Value.ExpiresAtUtc!.Value.Kind);
        Assert.Equal(expiry.Ticks, result.Value.ExpiresAtUtc.Value.Ticks);
    }

    [Fact]
    public async Task CreateLinkAsync_PastExpiry_Fails()
    {
        var file = AddFile(_ownerId);

        var result = await CreateService(_ownerId).CreateLinkAsync(new ShareLinkRequestDto(file.Id, DateTime.UtcNow.AddMinutes(-1)));

        Assert.Equal("Share.InvalidExpiry", result.Error.Code);
    }

    [Fact]
    public async Task OpenSharedFileAsync_ValidToken_ReturnsContentWithoutLogin()
    {
        var file = AddFile(_ownerId);
        var link = (await CreateService(_ownerId).CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;

        var result = await CreateService(null).OpenSharedFileAsync(link.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(file.FileName, result.Value.FileName);
    }

    [Fact]
    public async Task OpenSharedFileAsync_RevokedExpiredOrTrashed_ReturnsNotFound()
    {
        var file = AddFile(_ownerId);
        var service = CreateService(_ownerId);
        var revoked = (await service.CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;
        await service.RevokeLinkAsync(revoked.Id);
        var expired = AddLink(file.Id, DateTime.UtcNow.AddMinutes(-1));
        var live = (await service.CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;
        file.IsDeleted = true;
        await _context.SaveChangesAsync();

        foreach (var token in new[] { revoked.Token, expired, live.Token, "unknown" })
        {
            Assert.Equal("Share.NotFound", (await service.OpenSharedFileAsync(token)).Error.Code);
        }
    }

    [Fact]
    public async Task GetSharedFileInfoAsync_ValidAndRevokedTokens()
    {
        var file = AddFile(_ownerId);
        var service = CreateService(_ownerId);
        var link = (await service.CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;

        var info = (await service.GetSharedFileInfoAsync(link.Token)).Value;
        await service.RevokeLinkAsync(link.Id);

        Assert.Equal(("a.txt", "text/plain"), (info.FileName, info.ContentType));
        Assert.Equal("Share.NotFound", (await service.GetSharedFileInfoAsync(link.Token)).Error.Code);
    }

    [Fact]
    public async Task RevokeLinkAsync_OtherUser_ReturnsNotFound()
    {
        var file = AddFile(_ownerId);
        var link = (await CreateService(_ownerId).CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;

        var result = await CreateService(Guid.NewGuid()).RevokeLinkAsync(link.Id);

        Assert.Equal("Share.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task ListLinksAsync_ReturnsOnlyActiveUnexpiredLinks()
    {
        var file = AddFile(_ownerId);
        var service = CreateService(_ownerId);
        var kept = (await service.CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;
        var revoked = (await service.CreateLinkAsync(new ShareLinkRequestDto(file.Id, null))).Value;
        await service.RevokeLinkAsync(revoked.Id);
        AddLink(file.Id, DateTime.UtcNow.AddMinutes(-1));

        var links = (await service.ListLinksAsync(file.Id)).Value;

        Assert.Equal(kept.Id, Assert.Single(links).Id);
    }

    private FileMetadata AddFile(Guid ownerId)
    {
        var file = new FileMetadata { Id = Guid.NewGuid(), OwnerId = ownerId, FileName = "a.txt", ContentType = "text/plain", StorageKey = "k", FileSize = 1 };
        _context.Files.Add(file);
        _context.SaveChanges();
        return file;
    }

    private string AddLink(Guid fileId, DateTime expiresAtUtc)
    {
        var link = new SharedLink { Id = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"), FileMetadataId = fileId, CreatedById = _ownerId, ExpiresAtUtc = expiresAtUtc };
        _context.Links.Add(link);
        _context.SaveChanges();
        return link.Token;
    }

    private ShareService CreateService(Guid? userId) =>
        new(_context, new TestStorage(), new TestCurrentUser(userId?.ToString()));

    private sealed class TestCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId { get; } = userId;
        public bool IsAuthenticated => !string.IsNullOrEmpty(userId);
    }

    private sealed class TestStorage : IStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("x")));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestContext : DbContext, IApplicationDbContext
    {
        public DbSet<FileMetadata> Files => Set<FileMetadata>();
        public DbSet<SharedLink> Links => Set<SharedLink>();
        public DbSet<FileMetadata> FileMetadatas => Files;
        public DbSet<Folder> Folders => throw new NotSupportedException();
        public DbSet<FileTag> FileTags => throw new NotSupportedException();
        public DbSet<SharedLink> SharedLinks => Links;
        public DbSet<UploadSession> UploadSessions => throw new NotSupportedException();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileMetadata>().HasQueryFilter(file => !file.IsDeleted);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
