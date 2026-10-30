using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ArcanoCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class FileServiceTests
{
    [Fact]
    public async Task DownloadFileAsync_DoesNotDownloadAnotherUsersFile()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var file = new FileMetadata { Id = Guid.NewGuid(), OwnerId = ownerId, StorageKey = "key" };
        var storage = new TestStorage();
        var service = CreateService(new TestDbContext(file), storage, otherUserId);

        var result = await service.DownloadFileAsync(file.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("File.NotFound", result.Error.Code);
        Assert.Equal(0, storage.DownloadCalls);
    }

    [Fact]
    public async Task DeleteFileAsync_DoesNotDeleteAnotherUsersFile()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var file = new FileMetadata { Id = Guid.NewGuid(), OwnerId = ownerId, StorageKey = "key" };
        var storage = new TestStorage();
        var service = CreateService(new TestDbContext(file), storage, otherUserId);

        var result = await service.DeleteFileAsync(file.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("File.NotFound", result.Error.Code);
        Assert.Equal(0, storage.DeleteCalls);
    }

    [Fact]
    public async Task UploadFileAsync_ReturnsFailureWhenMetadataSaveFails()
    {
        var storage = new TestStorage();
        var service = CreateService(new TestDbContext(saveException: new InvalidOperationException("database unavailable")), storage, Guid.NewGuid());

        var result = await service.UploadFileAsync(new FileUploadDto(new MemoryStream([1]), "file.txt", "text/plain", 1));

        Assert.True(result.IsFailure);
        Assert.Equal("File.UploadFailed", result.Error.Code);
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, storage.DeleteCalls);
    }

    [Fact]
    public async Task DeleteFileAsync_ReturnsFailureWhenMetadataSaveFails()
    {
        var file = new FileMetadata { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), StorageKey = "key" };
        var storage = new TestStorage();
        var service = CreateService(new TestDbContext(file, new InvalidOperationException("database unavailable")), storage, file.OwnerId);

        var result = await service.DeleteFileAsync(file.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("File.DeleteFailed", result.Error.Code);
        Assert.Equal(0, storage.DeleteCalls);
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsOnlyCallersNonDeletedFiles_NewestFirst()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var older = NewFile(userId, "older.txt");
        var newer = NewFile(userId, "newer.txt");
        dbContext.FileMetadatas.AddRange(
            older,
            newer,
            NewFile(userId, "deleted.txt", isDeleted: true),
            NewFile(Guid.NewGuid(), "other.txt"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // SaveChangesAsync stamps CreatedAtUtc on insert, so set explicit values afterwards.
        older.CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        newer.CreatedAtUtc = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListFilesAsync(0, 50);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { newer.Id, older.Id }, result.Value.Select(f => f.Id).ToArray());
        Assert.Equal("newer.txt", result.Value[0].FileName);
        Assert.Equal("text/plain", result.Value[0].ContentType);
        Assert.Equal(10, result.Value[0].FileSize);
        Assert.Equal(newer.CreatedAtUtc, result.Value[0].CreatedAtUtc);
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsOnlyRequestedPage()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var oldest = NewFile(userId, "oldest.txt");
        var middle = NewFile(userId, "middle.txt");
        var newest = NewFile(userId, "newest.txt");
        dbContext.FileMetadatas.AddRange(oldest, middle, newest);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // SaveChangesAsync stamps CreatedAtUtc on insert, so set explicit values afterwards.
        oldest.CreatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        middle.CreatedAtUtc = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        newest.CreatedAtUtc = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListFilesAsync(1, 1);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal(middle.Id, item.Id);
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsEmptyList_WhenUserHasNoFiles()
    {
        await using var dbContext = CreateListDbContext();
        dbContext.FileMetadatas.Add(NewFile(Guid.NewGuid(), "other.txt"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, Guid.NewGuid().ToString()).ListFilesAsync(0, 50);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ListFilesAsync_ReturnsFailure_WhenCurrentUserIdIsInvalid(string? userId)
    {
        await using var dbContext = CreateListDbContext();

        var result = await CreateListService(dbContext, userId).ListFilesAsync(0, 50);

        Assert.True(result.IsFailure);
        Assert.Equal("File.Unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task UploadFileAsync_StoresFolderId_WhenFolderIsOwnedByCaller()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        dbContext.Folders.Add(folder);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var storage = new TestStorage();

        var result = await CreateListService(dbContext, userId.ToString(), storage)
            .UploadFileAsync(new FileUploadDto(new MemoryStream([1]), "file.txt", "text/plain", 1, folder.Id));

        Assert.True(result.IsSuccess);
        var saved = await dbContext.FileMetadatas.SingleAsync(f => f.Id == result.Value.Id);
        Assert.Equal(folder.Id, saved.FolderId);
        Assert.Equal(1, storage.UploadCalls);
    }

    [Fact]
    public async Task UploadFileAsync_ReturnsFolderNotFound_WhenFolderBelongsToAnotherUser()
    {
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(Guid.NewGuid());
        dbContext.Folders.Add(folder);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var storage = new TestStorage();

        var result = await CreateListService(dbContext, Guid.NewGuid().ToString(), storage)
            .UploadFileAsync(new FileUploadDto(new MemoryStream([1]), "file.txt", "text/plain", 1, folder.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Folder.NotFound", result.Error.Code);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsOnlyFilesInRequestedFolder()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folderA = NewFolder(userId);
        var folderB = NewFolder(userId);
        var inA = NewFile(userId, "a.txt", folderId: folderA.Id);
        dbContext.Folders.AddRange(folderA, folderB);
        dbContext.FileMetadatas.AddRange(inA, NewFile(userId, "b.txt", folderId: folderB.Id), NewFile(userId, "root.txt"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListFilesAsync(0, 50, folderA.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { inA.Id }, result.Value.Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsOnlyRootFiles_WhenFolderIdIsOmitted()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        var root = NewFile(userId, "root.txt");
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.AddRange(root, NewFile(userId, "inFolder.txt", folderId: folder.Id));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListFilesAsync(0, 50);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { root.Id }, result.Value.Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task MoveFileAsync_MovesFileToFolderAndBackToRoot()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        var file = NewFile(userId, "file.txt");
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = CreateListService(dbContext, userId.ToString());

        var toFolder = await service.MoveFileAsync(file.Id, folder.Id);
        Assert.True(toFolder.IsSuccess);
        Assert.Equal(folder.Id, file.FolderId);

        var toRoot = await service.MoveFileAsync(file.Id, null);
        Assert.True(toRoot.IsSuccess);
        Assert.Null(file.FolderId);
    }

    [Fact]
    public async Task MoveFileAsync_ReturnsFolderNotFound_WhenFolderBelongsToAnotherUser()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var foreignFolder = NewFolder(Guid.NewGuid());
        var file = NewFile(userId, "file.txt");
        dbContext.Folders.Add(foreignFolder);
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).MoveFileAsync(file.Id, foreignFolder.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Folder.NotFound", result.Error.Code);
        Assert.Null(file.FolderId);
    }

    private static ApplicationDbContext CreateListDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FileService CreateListService(ApplicationDbContext dbContext, string? userId, IStorageService? storage = null) =>
        new(dbContext, storage ?? new ListUnusedStorageService(), new ListCurrentUser(userId));

    private static FileMetadata NewFile(Guid ownerId, string name, bool isDeleted = false, Guid? folderId = null) => new()
    {
        Id = Guid.NewGuid(),
        FileName = name,
        ContentType = "text/plain",
        FileSize = 10,
        StorageKey = name,
        OwnerId = ownerId,
        IsDeleted = isDeleted,
        FolderId = folderId
    };

    private static Folder NewFolder(Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "folder",
        OwnerId = ownerId
    };

    private sealed class ListCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
        public bool IsAuthenticated => !string.IsNullOrEmpty(userId);
    }

    private sealed class ListUnusedStorageService : IStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static FileService CreateService(TestDbContext db, TestStorage storage, Guid userId) =>
        new(db, storage, new TestCurrentUser(userId));

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public string UserId { get; } = userId.ToString();
        public bool IsAuthenticated => true;
    }

    private sealed class TestStorage : IStorageService
    {
        public int UploadCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            return Task.FromResult("key");
        }

        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDbContext : IApplicationDbContext
    {
        private readonly FileMetadata[] _files;
        private readonly Exception? _saveException;

        public TestDbContext(FileMetadata? file = null, Exception? saveException = null)
        {
            _files = file is null ? [] : [file];
            _saveException = saveException;
            FileMetadatas = CreateFileMetadataSet(_files);
        }

        public DbSet<FileMetadata> FileMetadatas { get; }
        public DbSet<Folder> Folders => throw new NotSupportedException();
        public DbSet<FileTag> FileTags => throw new NotSupportedException();
        public DbSet<SharedLink> SharedLinks => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (_saveException is not null)
            {
                throw _saveException;
            }
            return Task.FromResult(1);
        }

        private static DbSet<FileMetadata> CreateFileMetadataSet(FileMetadata[] files)
        {
            var options = new DbContextOptionsBuilder<TestContext>().Options;
            var context = new TestContext(options, files);
            // Seed the in-memory store so FindAsync can see the row.
            context.Files.AddRange(files);
            context.SaveChanges();
            return context.Files;
        }
    }

    private sealed class TestContext(DbContextOptions<TestContext> options, FileMetadata[] files) : DbContext(options)
    {
        public DbSet<FileMetadata> Files => Set<FileMetadata>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileMetadata>().HasQueryFilter(file => !file.IsDeleted);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries().All(entry => entry.State != EntityState.Added))
            {
                foreach (var file in files)
                {
                    if (!Files.IgnoreQueryFilters().Any(existing => existing.Id == file.Id))
                    {
                        Files.Add(file);
                    }
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
