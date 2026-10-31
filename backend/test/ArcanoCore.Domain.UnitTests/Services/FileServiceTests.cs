using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
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

    private static FileMetadata Trashed(Guid ownerId, string name, DateTime deletedAtUtc, Guid? folderId = null)
    {
        var file = NewFile(ownerId, name, isDeleted: true, folderId: folderId);
        file.DeletedAtUtc = deletedAtUtc;
        return file;
    }

    [Fact]
    public async Task RenameFileAsync_TrimsAndRenames()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var file = NewFile(userId, "old.txt");
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).RenameFileAsync(file.Id, "  new.txt ");

        Assert.True(result.IsSuccess);
        Assert.Equal("new.txt", file.FileName);
        Assert.NotNull(file.LastModifiedAtUtc);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task RenameFileAsync_RejectsEmptyName(string name)
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var file = NewFile(userId, "old.txt");
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).RenameFileAsync(file.Id, name);

        Assert.Equal("File.InvalidName", result.Error.Code);
        Assert.Equal("old.txt", file.FileName);
    }

    [Fact]
    public async Task RenameFileAsync_ReturnsNotFound_ForAnotherUsersFile()
    {
        await using var dbContext = CreateListDbContext();
        var file = NewFile(Guid.NewGuid(), "old.txt");
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, Guid.NewGuid().ToString()).RenameFileAsync(file.Id, "new.txt");

        Assert.Equal("File.NotFound", result.Error.Code);
        Assert.Equal("old.txt", file.FileName);
    }

    [Fact]
    public async Task SetStarredAsync_TogglesStar()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var file = NewFile(userId, "file.txt");
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = CreateListService(dbContext, userId.ToString());

        Assert.True((await service.SetStarredAsync(file.Id, true)).IsSuccess);
        Assert.True(file.IsStarred);
        Assert.True((await service.SetStarredAsync(file.Id, false)).IsSuccess);
        Assert.False(file.IsStarred);
        Assert.Equal("File.NotFound", (await service.SetStarredAsync(Guid.NewGuid(), true)).Error.Code);
    }

    [Fact]
    public async Task ListFilesAsync_StarredView_ReturnsStarredFilesAcrossFolders()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        var rootStar = NewFile(userId, "a.txt");
        var folderStar = NewFile(userId, "b.txt", folderId: folder.Id);
        rootStar.IsStarred = true;
        folderStar.IsStarred = true;
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.AddRange(rootStar, folderStar, NewFile(userId, "plain.txt"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListFilesAsync(0, 50, view: "starred");

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { rootStar.Id, folderStar.Id }.Order(), result.Value.Select(f => f.Id).Order());
        Assert.All(result.Value, f => Assert.True(f.IsStarred));
    }

    [Fact]
    public async Task ListFilesAsync_RecentView_OrdersByLastModifiedThenCreated()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        var a = NewFile(userId, "a.txt", folderId: folder.Id);
        var b = NewFile(userId, "b.txt");
        var c = NewFile(userId, "c.txt");
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.AddRange(a, b, c);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = CreateListService(dbContext, userId.ToString());

        // SaveChangesAsync stamps LastModifiedAtUtc on update, so modify in the wanted order.
        await Task.Delay(20);
        await service.RenameFileAsync(b.Id, "b2.txt");
        await Task.Delay(20);
        await service.RenameFileAsync(a.Id, "a2.txt");

        var result = await service.ListFilesAsync(0, 50, view: "recent");

        Assert.Equal(new[] { a.Id, b.Id, c.Id }, result.Value.Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task ListFilesAsync_Query_SearchesAllFoldersCaseInsensitive()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        var inFolder = NewFile(userId, "Report-2024.txt", folderId: folder.Id);
        var root = NewFile(userId, "my report.txt");
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.AddRange(inFolder, root, NewFile(userId, "other.txt"), NewFile(Guid.NewGuid(), "report-foreign.txt"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListFilesAsync(0, 50, folder.Id, query: "REPORT");

        Assert.Equal(new[] { inFolder.Id, root.Id }.Order(), result.Value.Select(f => f.Id).Order());
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsInvalidView_ForUnknownView()
    {
        await using var dbContext = CreateListDbContext();

        var result = await CreateListService(dbContext, Guid.NewGuid().ToString()).ListFilesAsync(0, 50, view: "bogus");

        Assert.True(result.IsFailure);
        Assert.Equal("File.InvalidView", result.Error.Code);
    }

    [Fact]
    public async Task DeleteFileAsync_MovesFileToTrashWithoutDeletingStorage()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var file = NewFile(userId, "file.txt");
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var storage = new TestStorage();

        var result = await CreateListService(dbContext, userId.ToString(), storage).DeleteFileAsync(file.Id);

        Assert.True(result.IsSuccess);
        Assert.True(file.IsDeleted);
        Assert.NotNull(file.DeletedAtUtc);
        Assert.Equal(0, storage.DeleteCalls);
    }

    [Fact]
    public async Task ListTrashAsync_ReturnsOnlyCallersTrashedFiles()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var mine = Trashed(userId, "mine.txt", DateTime.UtcNow);
        dbContext.FileMetadatas.AddRange(mine, Trashed(Guid.NewGuid(), "theirs.txt", DateTime.UtcNow), NewFile(userId, "live.txt"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).ListTrashAsync(0, 50);

        Assert.Equal(new[] { mine.Id }, result.Value.Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task ListTrashAsync_PurgesItemsOlderThan30Days()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var expired = Trashed(userId, "expired.txt", DateTime.UtcNow.AddDays(-31));
        var fresh = Trashed(userId, "fresh.txt", DateTime.UtcNow.AddDays(-1));
        dbContext.FileMetadatas.AddRange(expired, fresh);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var storage = new TestStorage();

        var result = await CreateListService(dbContext, userId.ToString(), storage).ListTrashAsync(0, 50);

        Assert.Equal(new[] { fresh.Id }, result.Value.Select(f => f.Id).ToArray());
        Assert.Equal(1, storage.DeleteCalls);
        Assert.False(await dbContext.FileMetadatas.IgnoreQueryFilters().AnyAsync(f => f.Id == expired.Id));
    }

    [Fact]
    public async Task RestoreFileAsync_RestoresFileToItsFolder()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        var file = Trashed(userId, "file.txt", DateTime.UtcNow, folder.Id);
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).RestoreFileAsync(file.Id);

        Assert.True(result.IsSuccess);
        Assert.False(file.IsDeleted);
        Assert.Null(file.DeletedAtUtc);
        Assert.Equal(folder.Id, file.FolderId);
    }

    [Fact]
    public async Task RestoreFileAsync_MovesFileToRoot_WhenFolderIsDeleted()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var folder = NewFolder(userId);
        folder.IsDeleted = true;
        var file = Trashed(userId, "file.txt", DateTime.UtcNow, folder.Id);
        dbContext.Folders.Add(folder);
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).RestoreFileAsync(file.Id);

        Assert.True(result.IsSuccess);
        Assert.Null(file.FolderId);
    }

    [Fact]
    public async Task RestoreFileAsync_ReturnsNotFound_ForLiveFile()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var file = NewFile(userId, "file.txt");
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await CreateListService(dbContext, userId.ToString()).RestoreFileAsync(file.Id);

        Assert.Equal("File.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task PermanentlyDeleteFileAsync_RemovesRowAndDeletesStorageOnce()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateListDbContext();
        var file = Trashed(userId, "file.txt", DateTime.UtcNow);
        dbContext.FileMetadatas.Add(file);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var storage = new TestStorage();

        var result = await CreateListService(dbContext, userId.ToString(), storage).PermanentlyDeleteFileAsync(file.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, storage.DeleteCalls);
        Assert.False(await dbContext.FileMetadatas.IgnoreQueryFilters().AnyAsync(f => f.Id == file.Id));
    }

    [Fact]
    public async Task UploadFileAsync_ReturnsQuotaFailure_WithoutUploading()
    {
        await using var dbContext = CreateListDbContext();
        var storage = new TestStorage();
        var quota = new TestQuota { Result = Result.Failure(new Error("Storage.QuotaExceeded", "Quota exceeded.")) };

        var result = await CreateListService(dbContext, Guid.NewGuid().ToString(), storage, quota)
            .UploadFileAsync(new FileUploadDto(new MemoryStream([1]), "file.txt", "text/plain", 1));

        Assert.True(result.IsFailure);
        Assert.Equal("Storage.QuotaExceeded", result.Error.Code);
        Assert.Equal(0, storage.UploadCalls);
    }

    private static ApplicationDbContext CreateListDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FileService CreateListService(ApplicationDbContext dbContext, string? userId, IStorageService? storage = null, IStorageQuotaService? quota = null) =>
        new(dbContext, storage ?? new ListUnusedStorageService(), new ListCurrentUser(userId), quota ?? new TestQuota());

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
        new(db, storage, new TestCurrentUser(userId), new TestQuota());

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public string UserId { get; } = userId.ToString();
        public bool IsAuthenticated => true;
    }

    private sealed class TestQuota : IStorageQuotaService
    {
        public Result Result { get; set; } = Result.Success();

        public Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);

        public Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageUsageDto(0, long.MaxValue));
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
