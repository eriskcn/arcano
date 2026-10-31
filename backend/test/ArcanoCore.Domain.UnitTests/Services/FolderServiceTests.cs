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
        public Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Data.GetValueOrDefault(key) ?? []));
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

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FolderService CreateService(ApplicationDbContext dbContext, string? userId) =>
        new(dbContext, new FolderTestCurrentUser(userId), new FakeStorage());

    private sealed class FolderTestCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
        public bool IsAuthenticated => !string.IsNullOrEmpty(userId);
    }
}
