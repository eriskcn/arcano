using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ArcanoCore.API.UnitTests;

// Pins the HTTP status mapping of the sharing controllers; the service is a stub that returns a canned result.
public class UserSharesControllerTests
{
    private static readonly UserShareDto Share = new(Guid.NewGuid(), "File", Guid.NewGuid(), "a.txt", "b@example.com", DateTime.UtcNow);

    private static UserSharesController Controller(StubShares s) => new(s);

    [Fact]
    public async Task Create_NewShare_Returns201()
    {
        var r = Assert.IsType<ObjectResult>(await Controller(new() { Create = (Share, true) }).Create(new(null, null, null), default));
        Assert.Equal(201, r.StatusCode);
        Assert.Same(Share, r.Value);
    }

    [Fact]
    public async Task Create_ExistingShare_Returns200()
    {
        var r = Assert.IsType<OkObjectResult>(await Controller(new() { Create = (Share, false) }).Create(new(null, null, null), default));
        Assert.Same(Share, r.Value);
    }

    [Theory]
    [InlineData("UserShare.InvalidRecipient", 400)]
    [InlineData("UserShare.NotFound", 404)]
    [InlineData("Folder.NotFound", 404)]
    [InlineData("UserShare.Unauthorized", 401)]
    public async Task Create_Failure_MapsErrorCode(string code, int status)
    {
        var r = Assert.IsAssignableFrom<ObjectResult>(await Controller(new() { Fail = new Error(code, "x") }).Create(new(null, null, null), default));
        Assert.Equal(status, r.StatusCode);
        Assert.Equal(code, Assert.IsType<Error>(r.Value).Code);
    }

    [Fact]
    public async Task List_Success_Returns200_AndNotFound_Returns404()
    {
        Assert.IsType<OkObjectResult>(await Controller(new()).List(Guid.NewGuid(), null, default));
        Assert.IsType<NotFoundObjectResult>(await Controller(new() { Fail = new Error("UserShare.NotFound", "x") }).List(null, null, default));
    }

    [Fact]
    public async Task Delete_Success_Returns204_AndNotFound_Returns404()
    {
        Assert.IsType<NoContentResult>(await Controller(new()).Delete(Guid.NewGuid(), default));
        Assert.IsType<NotFoundObjectResult>(await Controller(new() { Fail = new Error("UserShare.NotFound", "x") }).Delete(Guid.NewGuid(), default));
    }
}

public class SharedControllerTests
{
    private static SharedController Controller(StubShares s) => new(s);
    private static readonly Error NotFound = new("UserShare.NotFound", "x");

    [Fact]
    public async Task Incoming_Returns200_Or404()
    {
        Assert.IsType<OkObjectResult>(await Controller(new()).Incoming(default));
        Assert.IsType<NotFoundObjectResult>(await Controller(new() { Fail = NotFound }).Incoming(default));
    }

    [Fact]
    public async Task Children_Returns200_Or404()
    {
        Assert.IsType<OkObjectResult>(await Controller(new()).Children(Guid.NewGuid(), null, 0, 50, default));
        Assert.IsType<NotFoundObjectResult>(await Controller(new() { Fail = NotFound }).Children(Guid.NewGuid(), null, 0, 50, default));
    }

    [Fact]
    public async Task DownloadFile_Success_ReturnsAttachmentStream_AndFailureReturns404()
    {
        var ok = Assert.IsType<FileStreamResult>(await Controller(new()).DownloadFile(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal("a.txt", ok.FileDownloadName);
        Assert.True(ok.EnableRangeProcessing);
        Assert.IsType<NotFoundObjectResult>(await Controller(new() { Fail = NotFound }).DownloadFile(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task DownloadFolder_ShareNotFound_Returns404()
    {
        var r = Assert.IsType<NotFoundObjectResult>(await Controller(new() { Fail = NotFound }).DownloadFolder(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal("UserShare.NotFound", Assert.IsType<Error>(r.Value).Code);
    }
}

// Every method succeeds with a canned value, or fails with Fail when it is set.
internal sealed class StubShares : IUserShareService
{
    public (UserShareDto, bool)? Create { get; init; }
    public Error? Fail { get; init; }

    private Result<T> R<T>(T value) => Fail is { } e ? Result<T>.Failure(e) : Result<T>.Success(value);
    private Result R() => Fail is { } e ? Result.Failure(e) : Result.Success();

    public Task<Result<(UserShareDto Share, bool Created)>> CreateAsync(UserShareRequestDto dto, CancellationToken ct = default) =>
        Task.FromResult(R(Create ?? (new UserShareDto(Guid.NewGuid(), "File", Guid.NewGuid(), "a", "b", DateTime.UtcNow), true)));
    public Task<Result<IReadOnlyList<UserShareRecipientDto>>> ListForItemAsync(Guid? fileId, Guid? folderId, CancellationToken ct = default) =>
        Task.FromResult(R<IReadOnlyList<UserShareRecipientDto>>([]));
    public Task<Result> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(R());
    public Task<Result<IReadOnlyList<IncomingShareDto>>> ListIncomingAsync(CancellationToken ct = default) =>
        Task.FromResult(R<IReadOnlyList<IncomingShareDto>>([]));
    public Task<Result<SharedChildrenDto>> GetChildrenAsync(Guid shareId, Guid? folderId, int skip, int take, CancellationToken ct = default) =>
        Task.FromResult(R(new SharedChildrenDto([], [])));
    public Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid shareId, Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(R<(Stream, string, string)>((new MemoryStream(), "text/plain", "a.txt")));
    public Task<Result<string>> GetFolderZipNameAsync(Guid shareId, Guid folderId, CancellationToken ct = default) => Task.FromResult(R("f.zip"));
    public Task<Result> WriteFolderZipAsync(Guid shareId, Guid folderId, Stream output, CancellationToken ct = default) => Task.FromResult(R());
}
