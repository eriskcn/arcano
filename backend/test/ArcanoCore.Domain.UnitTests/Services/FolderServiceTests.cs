using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ArcanoCore.Domain.UnitTests.Services;

public class FolderServiceTests
{
    [Fact]
    public async Task CreateFolderAsync_RejectsDuplicateRootName()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        await CreateFolderAsync(service, "Docs", null);

        var result = await service.CreateFolderAsync(new FolderRequestDto("Docs", null));

        Assert.True(result.IsFailure);
        Assert.Equal("Folder.NameTaken", result.Error.Code);
    }

    [Fact]
    public async Task CreateFolderAsync_RejectsDuplicateNameUnderSameParent()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        var parent = await CreateFolderAsync(service, "Projects", null);
        await CreateFolderAsync(service, "Notes", parent.Id);

        var result = await service.CreateFolderAsync(new FolderRequestDto("Notes", parent.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Folder.NameTaken", result.Error.Code);
    }

    [Fact]
    public async Task FolderOperations_DoNotAccessAnotherUsersFolder()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var owner = CreateService(dbContext, ownerId.ToString());
        var folder = await CreateFolderAsync(owner, "Mine", null);
        await CreateFolderAsync(owner, "Child", folder.Id);
        var other = CreateService(dbContext, otherUserId.ToString());

        var children = await other.ListFoldersAsync(folder.Id);
        var rename = await other.UpdateFolderAsync(folder.Id, new FolderRequestDto("Renamed", null));
        var delete = await other.DeleteFolderAsync(folder.Id);

        Assert.True(children.IsSuccess);
        Assert.Empty(children.Value);
        Assert.Equal("Folder.NotFound", rename.Error.Code);
        Assert.Equal("Folder.NotFound", delete.Error.Code);
    }

    [Fact]
    public async Task UpdateFolderAsync_RejectsMovingFolderIntoDescendant()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        var parent = await CreateFolderAsync(service, "A", null);
        var child = await CreateFolderAsync(service, "B", parent.Id);

        var result = await service.UpdateFolderAsync(parent.Id, new FolderRequestDto("A", child.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Folder.Cycle", result.Error.Code);
    }

    [Fact]
    public async Task DeleteAndZip_ExistingParentLoop_Terminate()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        var a = await CreateFolderAsync(service, "A", null);
        var b = await CreateFolderAsync(service, "B", null);
        (await dbContext.Folders.SingleAsync(f => f.Id == a.Id)).ParentId = b.Id;
        (await dbContext.Folders.SingleAsync(f => f.Id == b.Id)).ParentId = a.Id;
        await dbContext.SaveChangesAsync();

        // Task.Run: the in-memory provider completes queries synchronously, so a looping walk would never return a task to time out.
        var zip = await Task.Run(() => service.WriteZipAsync(a.Id, new MemoryStream())).WaitAsync(TimeSpan.FromSeconds(5));
        var delete = await Task.Run(() => service.DeleteFolderAsync(a.Id)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(zip.IsSuccess);
        Assert.True(delete.IsSuccess);
        Assert.All(await dbContext.Folders.IgnoreQueryFilters().ToListAsync(), f => Assert.True(f.IsDeleted));
    }

    [Fact]
    public async Task UpdateFolderAsync_ConcurrentCrossingMoves_OnlyOneSucceeds()
    {
        var userId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var gate = new SaveGate();
        await using var firstDb = CreateDbContext(dbName, gate);
        await using var secondDb = CreateDbContext(dbName);
        var first = CreateService(firstDb, userId.ToString());
        var second = CreateService(secondDb, userId.ToString());
        var a = await CreateFolderAsync(first, "A", null);
        var b = await CreateFolderAsync(first, "B", null);

        gate.Armed = true;
        var moveA = first.UpdateFolderAsync(a.Id, new FolderRequestDto("A", b.Id));
        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The first move has passed its cycle check and is paused before saving.
        var moveB = second.UpdateFolderAsync(b.Id, new FolderRequestDto("B", a.Id));
        await Task.Delay(200);
        gate.Release.SetResult();
        var results = await Task.WhenAll(moveA, moveB).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsFailure);
        Assert.Equal("Folder.Cycle", results[1].Error.Code);
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Reached.SetResult();
                await Release.Task;
            }

            return result;
        }
    }

    [Fact]
    public async Task DeleteFolderAsync_TrashesNestedFoldersAndFiles()
    {
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        var top = await CreateFolderAsync(service, "Top", null);
        var mid = await CreateFolderAsync(service, "Mid", top.Id);
        var leaf = await CreateFolderAsync(service, "Leaf", mid.Id);
        var sibling = await CreateFolderAsync(service, "Sibling", null);
        var other = await CreateFolderAsync(CreateService(dbContext, otherId.ToString()), "Other", null);
        AddFile(dbContext, userId, top.Id, "a");
        AddFile(dbContext, userId, leaf.Id, "b");
        AddFile(dbContext, userId, sibling.Id, "c");
        AddFile(dbContext, otherId, other.Id, "d");
        await dbContext.SaveChangesAsync();

        var result = await service.DeleteFolderAsync(top.Id);

        Assert.True(result.IsSuccess);
        var folders = await dbContext.Folders.IgnoreQueryFilters().ToListAsync();
        Assert.All(folders.Where(f => f.Id == top.Id || f.Id == mid.Id || f.Id == leaf.Id), f => Assert.True(f.IsDeleted));
        Assert.All(folders.Where(f => f.Id == sibling.Id || f.Id == other.Id), f => Assert.False(f.IsDeleted));
        var files = await dbContext.FileMetadatas.IgnoreQueryFilters().ToDictionaryAsync(f => f.StorageKey);
        Assert.True(files["a"].IsDeleted && files["a"].DeletedAtUtc != null);
        Assert.True(files["b"].IsDeleted && files["b"].DeletedAtUtc != null);
        Assert.False(files["c"].IsDeleted);
        Assert.False(files["d"].IsDeleted);
    }

    [Fact]
    public async Task WriteZipAsync_WritesTreeWithUniqueNames()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var storage = new FakeStorage();
        var service = new FolderService(dbContext, new FolderTestCurrentUser(userId.ToString()), storage);
        var top = await CreateFolderAsync(service, "Top", null);
        var sub = await CreateFolderAsync(service, "Sub", top.Id);
        await CreateFolderAsync(service, "Empty", top.Id);
        AddFile(dbContext, userId, top.Id, "k1", "a.txt");
        AddFile(dbContext, userId, top.Id, "k2", "a.txt");
        AddFile(dbContext, userId, sub.Id, "k3", "../b.txt");
        var trashed = AddFile(dbContext, userId, top.Id, "k4", "gone.txt");
        trashed.IsDeleted = true;
        await dbContext.SaveChangesAsync();
        storage.Data["k1"] = "one"u8.ToArray();
        storage.Data["k2"] = "two"u8.ToArray();
        storage.Data["k3"] = "three"u8.ToArray();

        using var output = new MemoryStream();
        var result = await service.WriteZipAsync(top.Id, output);

        Assert.True(result.IsSuccess);
        output.Position = 0;
        using var zip = new ZipArchive(output);
        var names = zip.Entries.Select(e => e.FullName).Order().ToList();
        Assert.Equal(new[] { "Empty/", "Sub/", "Sub/.._b.txt", "a (2).txt", "a.txt" }.Order(), names);
        using var reader = new StreamReader(zip.GetEntry("Sub/.._b.txt")!.Open());
        Assert.Equal("three", reader.ReadToEnd());
    }

    [Fact]
    public async Task WriteZipAsync_StorageFailsMidCopy_LeavesInvalidArchive()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var storage = new FakeStorage { FailKey = "k2" };
        var service = new FolderService(dbContext, new FolderTestCurrentUser(userId.ToString()), storage);
        var top = await CreateFolderAsync(service, "Top", null);
        AddFile(dbContext, userId, top.Id, "k1", "a.txt");
        AddFile(dbContext, userId, top.Id, "k2", "b.txt");
        await dbContext.SaveChangesAsync();
        storage.Data["k1"] = "one"u8.ToArray();

        using var output = new MemoryStream();
        await Assert.ThrowsAsync<IOException>(() => service.WriteZipAsync(top.Id, output));

        output.Position = 0;
        Assert.Throws<InvalidDataException>(() => new ZipArchive(output));
    }

    [Fact]
    public async Task WriteZipAsync_ChildNamedBeforeParent_Succeeds()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var storage = new FakeStorage();
        var service = new FolderService(dbContext, new FolderTestCurrentUser(userId.ToString()), storage);
        var top = await CreateFolderAsync(service, "Top", null);
        var photos = await CreateFolderAsync(service, "Photos", top.Id);
        var year = await CreateFolderAsync(service, "2024", photos.Id);
        AddFile(dbContext, userId, year.Id, "k1", "a.txt");
        await dbContext.SaveChangesAsync();
        storage.Data["k1"] = "one"u8.ToArray();

        using var output = new MemoryStream();
        var result = await service.WriteZipAsync(top.Id, output);

        Assert.True(result.IsSuccess);
        output.Position = 0;
        using var zip = new ZipArchive(output);
        Assert.NotNull(zip.GetEntry("Photos/2024/a.txt"));
    }

    [Fact]
    public async Task WriteZipAsync_OtherUsersFolder_IsNotFound()
    {
        var ownerId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var folder = await CreateFolderAsync(CreateService(dbContext, ownerId.ToString()), "Mine", null);
        var other = CreateService(dbContext, Guid.NewGuid().ToString());

        var result = await other.WriteZipAsync(folder.Id, new MemoryStream());

        Assert.Equal("Folder.NotFound", result.Error.Code);
    }

    private static FileMetadata AddFile(ApplicationDbContext dbContext, Guid ownerId, Guid folderId, string key, string name = "f.txt")
    {
        var file = new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = name,
            ContentType = "text/plain",
            FileSize = 1,
            StorageKey = key,
            OwnerId = ownerId,
            FolderId = folderId
        };
        dbContext.FileMetadatas.Add(file);
        return file;
    }

    private sealed class FakeStorage : IStorageService
    {
        public Dictionary<string, byte[]> Data { get; } = new();
        public string? FailKey { get; set; }
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) =>
            key == FailKey ? throw new IOException("storage failed") : Task.FromResult<Stream>(new MemoryStream(Data.GetValueOrDefault(key) ?? []));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task DeleteFolderAsync_SoftDeletesEmptyFolder()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        var folder = await CreateFolderAsync(service, "Empty", null);

        var result = await service.DeleteFolderAsync(folder.Id);

        Assert.True(result.IsSuccess);
        var stored = await dbContext.Folders.IgnoreQueryFilters().SingleAsync(f => f.Id == folder.Id);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task SetStarredAsync_OwnerStars_OtherUserGetsNotFound()
    {
        await using var dbContext = CreateDbContext();
        var owner = CreateService(dbContext, Guid.NewGuid().ToString());
        var other = CreateService(dbContext, Guid.NewGuid().ToString());
        var folder = await CreateFolderAsync(owner, "Mine", null);

        var denied = await other.SetStarredAsync(folder.Id, true);
        Assert.True(denied.IsFailure);
        Assert.Equal("Folder.NotFound", denied.Error.Code);

        Assert.True((await owner.SetStarredAsync(folder.Id, true)).IsSuccess);
        Assert.True((await owner.ListFoldersAsync(null)).Value.Single().IsStarred);
    }

    private static async Task<FolderDto> CreateFolderAsync(IFolderService service, string name, Guid? parentId) =>
        (await service.CreateFolderAsync(new FolderRequestDto(name, parentId))).Value;

    private static ApplicationDbContext CreateDbContext(string? dbName = null, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString());
        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private static FolderService CreateService(ApplicationDbContext dbContext, string? userId) =>
        new(dbContext, new FolderTestCurrentUser(userId), new FakeStorage());

    private sealed class FolderTestCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
        public bool IsAuthenticated => !string.IsNullOrEmpty(userId);
    }
}
