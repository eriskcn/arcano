using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using ArcanoCore.Infrastructure;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ArcanoCore.API.UnitTests;

// Runs the real "Bearer" and "Download" schemes from AddInfrastructure against an in-memory user store.
public class DownloadTicketTests
{
    private const string Secret = "test-secret-key-that-is-at-least-32-bytes-long";
    private const string FilePath = "/api/Files/7d1f2f4e-0000-0000-0000-000000000001";

    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Secret,
            ["ConnectionStrings:DefaultConnection"] = "Host=unused",
            ["Storage:ServiceUrl"] = "http://unused",
            ["Storage:AccessKey"] = "k",
            ["Storage:SecretKey"] = "s"
        })
        .Build();

    private readonly ServiceProvider _services;
    private readonly IdentityUser<Guid> _user = new() { Id = Guid.NewGuid(), UserName = "a@example.com", Email = "a@example.com", SecurityStamp = "stamp-1" };

    public DownloadTicketTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddInfrastructure(_config);
        services.AddSingleton<IUserStore<IdentityUser<Guid>>, AuthControllerTests.InMemoryUserStore>(); // instead of the EF store
        _services = services.BuildServiceProvider();
        _services.GetRequiredService<IUserStore<IdentityUser<Guid>>>().CreateAsync(_user, default).GetAwaiter().GetResult();
    }

    private ClaimsPrincipal Session => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()), new Claim(AuthClaims.SecurityStamp, _user.SecurityStamp!)], "t"));

    private string Ticket(string path = FilePath) => JwtTokens.CreateDownloadTicket(_config, Session, path);

    private async Task<bool> Authenticates(string scheme, string path = FilePath, string? dt = null, string? bearer = null, string method = "GET")
    {
        using var scope = _services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = path;
        if (dt != null) context.Request.QueryString = QueryString.Create(DownloadTicket.QueryKey, dt);
        if (bearer != null) context.Request.Headers.Authorization = "Bearer " + bearer;
        return (await context.AuthenticateAsync(scheme)).Succeeded;
    }

    [Fact]
    public async Task Ticket_OnItsPath_Passes_IgnoringCaseAndTrailingSlash()
    {
        Assert.True(await Authenticates(DownloadTicket.Scheme, dt: Ticket()));
        Assert.True(await Authenticates(DownloadTicket.Scheme, FilePath.ToUpperInvariant() + "/", dt: Ticket()));
    }

    [Fact]
    public async Task Ticket_WrongPathOrMethod_Fails()
    {
        Assert.False(await Authenticates(DownloadTicket.Scheme, "/api/Files/7d1f2f4e-0000-0000-0000-000000000002", dt: Ticket()));
        Assert.False(await Authenticates(DownloadTicket.Scheme, FilePath + "/ticket", dt: Ticket()));
        Assert.False(await Authenticates(DownloadTicket.Scheme, dt: Ticket(), method: "POST"));
        Assert.False(await Authenticates(DownloadTicket.Scheme, dt: Ticket(), method: "DELETE"));
    }

    [Fact]
    public async Task Ticket_AfterStampChangeOrDisable_Fails()
    {
        var ticket = Ticket();
        _user.SecurityStamp = "stamp-2"; // sign out everywhere / password change
        Assert.False(await Authenticates(DownloadTicket.Scheme, dt: ticket));

        _user.SecurityStamp = "stamp-1";
        Assert.True(await Authenticates(DownloadTicket.Scheme, dt: ticket));
        _user.LockoutEnd = DateTimeOffset.MaxValue; // disabled by an admin
        Assert.False(await Authenticates(DownloadTicket.Scheme, dt: ticket));
    }

    [Fact]
    public async Task ExpiredTicket_Fails()
    {
        var token = new JwtSecurityToken("ArcanoCore", DownloadTicket.Audience,
            [new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()), new Claim(AuthClaims.SecurityStamp, _user.SecurityStamp!), new Claim(DownloadTicket.PathClaim, FilePath)],
            notBefore: DateTime.UtcNow.AddMinutes(-2), expires: DateTime.UtcNow.AddSeconds(-1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));

        Assert.False(await Authenticates(DownloadTicket.Scheme, dt: new JwtSecurityTokenHandler().WriteToken(token)));
    }

    [Fact]
    public async Task SessionTokenAndTicket_AreNotInterchangeable()
    {
        var session = JwtTokens.Create(_config, _user, 0);
        Assert.True(await Authenticates(JwtBearerDefaults.AuthenticationScheme, bearer: session));

        Assert.False(await Authenticates(DownloadTicket.Scheme, dt: session)); // session JWT as ?dt=
        Assert.False(await Authenticates(JwtBearerDefaults.AuthenticationScheme, bearer: Ticket())); // ticket as a session token
        Assert.False(await Authenticates(DownloadTicket.Scheme, bearer: Ticket())); // the Download scheme ignores the header
    }

    [Fact]
    public async Task FileTicket_ReturnsServiceError_OrATicketForExactlyTheDownloadPath()
    {
        var missing = Assert.IsType<NotFoundObjectResult>(await new FilesController(new StubFiles { Fail = new Error("File.NotFound", "x") }).Ticket(Guid.NewGuid(), _config, default));
        Assert.Equal("File.NotFound", Assert.IsType<Error>(missing.Value).Code);

        var controller = WithRequest(new FilesController(new StubFiles()), FilePath + "/ticket");
        Assert.Equal(FilePath, TicketPath(await controller.Ticket(Guid.NewGuid(), _config, default)));
    }

    [Fact]
    public async Task FolderTicket_TooLarge_ReturnsError_ElseTicketForTheZipPath()
    {
        var tooLarge = Assert.IsType<BadRequestObjectResult>(await new FoldersController(new StubFolders { Fail = new Error("Folder.TooLarge", "x") }).Ticket(Guid.NewGuid(), _config, default));
        Assert.Equal("Folder.TooLarge", Assert.IsType<Error>(tooLarge.Value).Code);

        const string zip = "/api/Folders/7d1f2f4e-0000-0000-0000-000000000003/download";
        Assert.Equal(zip, TicketPath(await WithRequest(new FoldersController(new StubFolders()), zip + "/ticket").Ticket(Guid.NewGuid(), _config, default)));
    }

    [Fact]
    public async Task SharedTickets_MapErrors_ElseTicketForTheDownloadPath()
    {
        Assert.IsType<NotFoundObjectResult>(await new SharedController(new StubShares { Fail = new Error("UserShare.NotFound", "x") }).FileTicket(Guid.NewGuid(), Guid.NewGuid(), _config, default));
        Assert.IsType<BadRequestObjectResult>(await new SharedController(new StubShares { Fail = new Error("Folder.TooLarge", "x") }).FolderTicket(Guid.NewGuid(), Guid.NewGuid(), _config, default));

        const string file = "/api/Shared/7d1f2f4e-0000-0000-0000-000000000004/files/7d1f2f4e-0000-0000-0000-000000000005/download";
        Assert.Equal(file, TicketPath(await WithRequest(new SharedController(new StubShares()), file + "/ticket").FileTicket(Guid.NewGuid(), Guid.NewGuid(), _config, default)));
    }

    private T WithRequest<T>(T controller, string path) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Session } };
        controller.HttpContext.Request.Path = path;
        return controller;
    }

    private static string TicketPath(IActionResult result)
    {
        var value = Assert.IsType<OkObjectResult>(result).Value!;
        var ticket = (string)value.GetType().GetProperty("ticket")!.GetValue(value)!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(ticket);
        Assert.Contains(DownloadTicket.Audience, jwt.Audiences);
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddSeconds(61));
        return jwt.Claims.Single(c => c.Type == DownloadTicket.PathClaim).Value;
    }

    private sealed class StubFiles : IFileService
    {
        public Error? Fail { get; init; }

        public Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Fail is { } e ? Result<(Stream, string, string)>.Failure(e) : Result<(Stream, string, string)>.Success((new MemoryStream(), "text/plain", "a.txt")));

        public Task<Result<FileResponseDto>> UploadFileAsync(FileUploadDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<FileListItemDto>>> ListFilesAsync(int skip, int take, Guid? folderId = null, string? view = null, string? query = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> DeleteFileAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> RenameFileAsync(Guid id, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> SetStarredAsync(Guid id, bool isStarred, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<FileListItemDto>>> ListTrashAsync(int skip, int take, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> RestoreFileAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> PermanentlyDeleteFileAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> EmptyTrashAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> MoveFileAsync(Guid id, Guid? folderId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<FileResponseDto>> CopyFileAsync(Guid id, Guid? folderId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubFolders : IFolderService
    {
        public Error? Fail { get; init; }

        public Task<Result<string>> GetZipNameAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Fail is { } e ? Result<string>.Failure(e) : Result<string>.Success("f.zip"));

        public Task<Result<FolderDto>> CreateFolderAsync(FolderRequestDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<FolderDto>>> ListFoldersAsync(Guid? parentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<FolderDto>> UpdateFolderAsync(Guid id, FolderRequestDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> SetStarredAsync(Guid id, bool isStarred, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> DeleteFolderAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<string>> GetZipNameForOwnerAsync(Guid ownerId, Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> WriteZipAsync(Guid id, Stream output, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result> WriteZipForOwnerAsync(Guid ownerId, Guid id, Stream output, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
