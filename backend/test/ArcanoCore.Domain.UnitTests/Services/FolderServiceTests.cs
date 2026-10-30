using System;
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
    public async Task DeleteFolderAsync_RejectsFolderWithFiles()
    {
        var userId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, userId.ToString());
        var folder = await CreateFolderAsync(service, "Docs", null);
        dbContext.FileMetadatas.Add(new FileMetadata
        {
            Id = Guid.NewGuid(),
            FileName = "a.txt",
            ContentType = "text/plain",
            FileSize = 1,
            StorageKey = "a",
            OwnerId = userId,
            FolderId = folder.Id
        });
        await dbContext.SaveChangesAsync();

        var result = await service.DeleteFolderAsync(folder.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Folder.NotEmpty", result.Error.Code);
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
        new(dbContext, new FolderTestCurrentUser(userId));

    private sealed class FolderTestCurrentUser(string? userId) : ICurrentUser
    {
        public string? UserId => userId;
        public bool IsAuthenticated => !string.IsNullOrEmpty(userId);
    }
}
