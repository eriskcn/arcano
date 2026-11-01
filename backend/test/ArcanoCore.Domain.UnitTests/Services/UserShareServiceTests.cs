using System.IO.Compression;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class UserShareServiceTests
{
    private readonly TestContext _db = new();
    private readonly FakeDirectory _dir = new();
    private readonly FakeStorage _storage = new();
    private readonly Guid _owner;
    private readonly Guid _bob;
    private readonly Guid _eve;

    public UserShareServiceTests()
    {
        _owner = _dir.Add("owner@x.com");
        _bob = _dir.Add("bob@x.com");
        _eve = _dir.Add("eve@x.com");
    }

    private UserShareService As(Guid user) => new(_db, new User(user), _dir, _storage, new FolderService(_db, new User(user), _storage));

    private Folder AddFolder(string name, Guid? parent = null, Guid? owner = null)
    {
        var f = new Folder { Id = Guid.NewGuid(), Name = name, OwnerId = owner ?? _owner, ParentId = parent };
        _db.Folders.Add(f);
        return f;
    }

    private FileMetadata AddFile(string name, Guid? folder = null, Guid? owner = null)
    {
        var f = new FileMetadata { Id = Guid.NewGuid(), FileName = name, ContentType = "text/plain", FileSize = 3, StorageKey = "k-" + name, OwnerId = owner ?? _owner, FolderId = folder };
        _db.FileMetadatas.Add(f);
        _storage.Data[f.StorageKey] = [1, 2, 3];
        return f;
    }

    private async Task<Guid> Share(Guid? fileId, Guid? folderId, Guid? to = null)
    {
        await _db.SaveChangesAsync(default);
        var r = await As(_owner).CreateAsync(new(fileId, folderId, _dir.EmailOf(to ?? _bob)));
        Assert.True(r.IsSuccess, r.Error.Code);
        return r.Value.Share.Id;
    }

    [Fact]
    public async Task Create_Folder_ThenDuplicateIsIdempotent()
    {
        var folder = AddFolder("docs");
        await _db.SaveChangesAsync(default);

        var first = await As(_owner).CreateAsync(new(null, folder.Id, "BOB@x.com"));
        var second = await As(_owner).CreateAsync(new(null, folder.Id, "bob@x.com"));

        Assert.True(first.Value.Created);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Share.Id, second.Value.Share.Id);
        Assert.Equal(("folder", "docs", "bob@x.com"), (first.Value.Share.Kind, first.Value.Share.Name, first.Value.Share.RecipientEmail));
        Assert.Single(_db.UserShares);
    }

    [Fact]
    public async Task Create_SelfUnknownDisabledAndMalformedRecipients_AllGetTheSameGenericError()
    {
        var file = AddFile("a.txt");
        await _db.SaveChangesAsync(default);
        _dir.Disable(_eve);

        foreach (var email in new[] { "owner@x.com", "nobody@x.com", "eve@x.com", "not-an-email", null })
        {
            var r = await As(_owner).CreateAsync(new(file.Id, null, email));
            Assert.Equal("UserShare.InvalidRecipient", r.Error.Code);
        }
        Assert.Empty(_db.UserShares);
    }

    [Fact]
    public async Task Create_NeedsExactlyOneTarget_AndOwnedLiveItem()
    {
        var mine = AddFile("a.txt");
        var theirs = AddFile("b.txt", owner: _bob);
        var trashed = AddFile("c.txt"); trashed.IsDeleted = true;
        await _db.SaveChangesAsync(default);

        Assert.Equal("UserShare.InvalidTarget", (await As(_owner).CreateAsync(new(null, null, "bob@x.com"))).Error.Code);
        Assert.Equal("UserShare.InvalidTarget", (await As(_owner).CreateAsync(new(mine.Id, Guid.NewGuid(), "bob@x.com"))).Error.Code);
        Assert.Equal("UserShare.NotFound", (await As(_owner).CreateAsync(new(theirs.Id, null, "eve@x.com"))).Error.Code);
        Assert.Equal("UserShare.NotFound", (await As(_owner).CreateAsync(new(trashed.Id, null, "bob@x.com"))).Error.Code);
    }

    [Fact]
    public async Task Recipient_CannotReshare_AndNonOwnerCannotListOrRevoke()
    {
        var file = AddFile("a.txt");
        var id = await Share(file.Id, null);

        Assert.Equal("UserShare.NotFound", (await As(_bob).CreateAsync(new(file.Id, null, "eve@x.com"))).Error.Code);
        Assert.Equal("UserShare.NotFound", (await As(_bob).ListForItemAsync(file.Id, null)).Error.Code);
        Assert.Equal("UserShare.NotFound", (await As(_eve).DeleteAsync(id)).Error.Code);
        Assert.Single(_db.UserShares);

        var list = await As(_owner).ListForItemAsync(file.Id, null);
        Assert.Equal("bob@x.com", list.Value.Single().RecipientEmail);
    }

    [Fact]
    public async Task Revoke_ByOwnerOrRecipient_BlocksAccessImmediately()
    {
        var file = AddFile("a.txt");
        var id = await Share(file.Id, null);
        Assert.True((await As(_bob).DownloadFileAsync(id, file.Id)).IsSuccess);

        Assert.True((await As(_owner).DeleteAsync(id)).IsSuccess);
        Assert.Equal("UserShare.NotFound", (await As(_bob).DownloadFileAsync(id, file.Id)).Error.Code);

        var id2 = await Share(file.Id, null);
        Assert.True((await As(_bob).DeleteAsync(id2)).IsSuccess);
        Assert.Empty(_db.UserShares);
    }

    [Fact]
    public async Task Recipient_SeesOnlyTheSharedSubtree()
    {
        var root = AddFolder("root");
        var sub = AddFolder("sub", root.Id);
        var sibling = AddFolder("sibling");
        var inSub = AddFile("deep.txt", sub.Id);
        var inSibling = AddFile("secret.txt", sibling.Id);
        var loose = AddFile("loose.txt");
        var id = await Share(null, root.Id);
        var svc = As(_bob);

        var top = (await svc.GetChildrenAsync(id, null, 0, 50)).Value;
        Assert.Equal(["sub"], top.Folders.Select(f => f.Name));
        Assert.Equal(["deep.txt"], (await svc.GetChildrenAsync(id, sub.Id, 0, 50)).Value.Files.Select(f => f.FileName));

        Assert.Equal("UserShare.NotFound", (await svc.GetChildrenAsync(id, sibling.Id, 0, 50)).Error.Code);
        Assert.Equal("UserShare.NotFound", (await svc.GetChildrenAsync(id, Guid.NewGuid(), 0, 50)).Error.Code);
        Assert.True((await svc.DownloadFileAsync(id, inSub.Id)).IsSuccess);
        Assert.Equal("UserShare.NotFound", (await svc.DownloadFileAsync(id, inSibling.Id)).Error.Code);
        Assert.Equal("UserShare.NotFound", (await svc.DownloadFileAsync(id, loose.Id)).Error.Code);
        Assert.Equal("UserShare.NotFound", (await svc.GetFolderZipNameAsync(id, sibling.Id)).Error.Code);
        Assert.Equal("root.zip", (await svc.GetFolderZipNameAsync(id, root.Id)).Value);
    }

    [Fact]
    public async Task FileShare_GrantsOnlyThatFile_AndNoBrowsing()
    {
        var folder = AddFolder("f");
        var shared = AddFile("a.txt", folder.Id);
        var other = AddFile("b.txt", folder.Id);
        var id = await Share(shared.Id, null);

        Assert.True((await As(_bob).DownloadFileAsync(id, shared.Id)).IsSuccess);
        Assert.False((await As(_bob).DownloadFileAsync(id, other.Id)).IsSuccess);
        Assert.False((await As(_bob).GetChildrenAsync(id, null, 0, 50)).IsSuccess);
        Assert.False((await As(_bob).GetFolderZipNameAsync(id, folder.Id)).IsSuccess);
    }

    [Fact]
    public async Task OtherUsers_CannotUseSomeoneElsesShareId()
    {
        var file = AddFile("a.txt");
        var id = await Share(file.Id, null);

        Assert.Equal("UserShare.NotFound", (await As(_eve).DownloadFileAsync(id, file.Id)).Error.Code);
        Assert.Equal("UserShare.NotFound", (await As(_owner).DownloadFileAsync(id, file.Id)).Error.Code);
        Assert.Empty((await As(_eve).ListIncomingAsync()).Value);
    }

    [Fact]
    public async Task TrashedItems_AndDisabledUsers_AreHidden()
    {
        var root = AddFolder("root");
        var sub = AddFolder("sub", root.Id);
        var file = AddFile("a.txt", sub.Id);
        var fileShare = AddFile("solo.txt");
        var id = await Share(null, root.Id);
        await Share(fileShare.Id, null);
        Assert.Equal(2, (await As(_bob).ListIncomingAsync()).Value.Count);

        file.IsDeleted = true;
        await _db.SaveChangesAsync(default);
        Assert.Equal("UserShare.NotFound", (await As(_bob).DownloadFileAsync(id, file.Id)).Error.Code);
        sub.IsDeleted = true;
        await _db.SaveChangesAsync(default);
        Assert.Equal("UserShare.NotFound", (await As(_bob).GetChildrenAsync(id, sub.Id, 0, 50)).Error.Code);
        fileShare.IsDeleted = true;
        await _db.SaveChangesAsync(default);
        Assert.Equal(["root"], (await As(_bob).ListIncomingAsync()).Value.Select(s => s.Name));

        root.IsDeleted = true;
        await _db.SaveChangesAsync(default);
        Assert.Empty((await As(_bob).ListIncomingAsync()).Value);
        Assert.Equal("UserShare.NotFound", (await As(_bob).GetChildrenAsync(id, null, 0, 50)).Error.Code);

        root.IsDeleted = false;
        await _db.SaveChangesAsync(default);
        Assert.True((await As(_bob).GetChildrenAsync(id, null, 0, 50)).IsSuccess);
        _dir.Disable(_owner);
        Assert.Equal("UserShare.NotFound", (await As(_bob).GetChildrenAsync(id, null, 0, 50)).Error.Code);
        Assert.Empty((await As(_bob).ListIncomingAsync()).Value);
        _dir.Enable(_owner);
        _dir.Disable(_bob);
        Assert.Equal("UserShare.NotFound", (await As(_bob).GetChildrenAsync(id, null, 0, 50)).Error.Code);
    }

    [Fact]
    public async Task Incoming_ShowsKindSizeAndOwner()
    {
        var folder = AddFolder("docs");
        var file = AddFile("a.txt");
        await Share(file.Id, null);
        await Share(null, folder.Id);

        var items = (await As(_bob).ListIncomingAsync()).Value;

        var f = items.Single(i => i.Kind == "file");
        Assert.Equal((3L, "text/plain", "owner@x.com"), (f.SizeBytes, f.ContentType, f.OwnerEmail));
        var d = items.Single(i => i.Kind == "folder");
        Assert.Null(d.SizeBytes);
        Assert.Null(d.ContentType);
    }

    [Fact]
    public async Task FolderZip_ContainsOnlyTheSubtree_FromTheOwner()
    {
        var root = AddFolder("root");
        var sub = AddFolder("sub", root.Id);
        AddFile("a.txt", root.Id);
        AddFile("b.txt", sub.Id);
        AddFile("outside.txt");
        var id = await Share(null, root.Id);

        using var ms = new MemoryStream();
        var result = await As(_bob).WriteFolderZipAsync(id, sub.Id, ms);
        Assert.True(result.IsSuccess);
        ms.Position = 0;
        using var zip = new ZipArchive(ms);
        Assert.Equal(["b.txt"], zip.Entries.Select(e => e.FullName));

        using var all = new MemoryStream();
        await As(_bob).WriteFolderZipAsync(id, root.Id, all);
        all.Position = 0;
        Assert.Equal(["a.txt", "sub/", "sub/b.txt"], new ZipArchive(all).Entries.Select(e => e.FullName).Order());

        Assert.Equal("UserShare.NotFound", (await As(_eve).WriteFolderZipAsync(id, root.Id, new MemoryStream())).Error.Code);
    }

    private sealed class User(Guid id) : ICurrentUser
    {
        public string? UserId => id.ToString();
        public bool IsAuthenticated => true;
    }

    private sealed class FakeDirectory : IUserDirectory
    {
        private readonly Dictionary<Guid, DirectoryUser> _users = new();

        public Guid Add(string email)
        {
            var id = Guid.NewGuid();
            _users[id] = new DirectoryUser(id, email, false);
            return id;
        }

        public string EmailOf(Guid id) => _users[id].Email;
        public void Disable(Guid id) => _users[id] = _users[id] with { IsDisabled = true };
        public void Enable(Guid id) => _users[id] = _users[id] with { IsDisabled = false };

        public Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.Values.FirstOrDefault(u => u.Email == email));

        public Task<Dictionary<Guid, DirectoryUser>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult(ids.Where(_users.ContainsKey).Distinct().ToDictionary(i => i, i => _users[i]));
    }

    private sealed class FakeStorage : IStorageService
    {
        public Dictionary<string, byte[]> Data { get; } = new();
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Data[key]));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestContext : DbContext, IApplicationDbContext
    {
        public DbSet<FileMetadata> FileMetadatas => Set<FileMetadata>();
        public DbSet<Folder> Folders => Set<Folder>();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();
        public DbSet<UploadSession> UploadSessions => throw new NotSupportedException();
        public DbSet<UserProfile> UserProfiles => throw new NotSupportedException();
        public DbSet<InviteKey> InviteKeys => throw new NotSupportedException();
        public DbSet<UserShare> UserShares => Set<UserShare>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileMetadata>().HasQueryFilter(f => !f.IsDeleted);
            modelBuilder.Entity<Folder>().HasQueryFilter(f => !f.IsDeleted);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
}
