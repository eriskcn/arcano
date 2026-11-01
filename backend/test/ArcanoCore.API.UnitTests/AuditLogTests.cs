using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArcanoCore.API.Authorization;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class AuditLogTests
{
    private const string Password = "Passw0rd!";

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed class Caller(Guid id) : ICurrentUser
    {
        public string? UserId { get; } = id.ToString();
        public bool IsAuthenticated => true;
    }

    private sealed class NoAccounts : IAccountService
    {
        public Task DeleteUserDataAsync(Guid ownerId, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task KeyLifecycle_IsAudited_WithoutKeyMaterial()
    {
        var env = AuthControllerTests.CreateEnv();
        var authLog = new ListLogger<AuthController>();
        var auth = new AuthController(env.UserManager, env.Configuration, env.Db, env.Keys, authLog);
        Assert.IsType<OkObjectResult>(await auth.Register(new RegisterRequest("a@example.com", Password)));
        var adminId = (await env.UserManager.FindByEmailAsync("a@example.com"))!.Id;
        var adminLog = new ListLogger<AdminController>();
        var admin = new AdminController(env.UserManager, env.Db, new StorageQuotaService(env.Db, 100), new NoAccounts(), env.Keys, new Caller(adminId), adminLog);

        var created = Assert.IsType<ObjectResult>(await admin.CreateKey(new CreateKeyRequest("Join", null, null, null), default));
        var plain = (string)created.Value!.GetType().GetProperty("Key")!.GetValue(created.Value)!;
        Assert.IsType<OkObjectResult>(await auth.Register(new RegisterRequest("b@example.com", Password, plain)));
        var revokable = Assert.IsType<ObjectResult>(await admin.CreateKey(new CreateKeyRequest("Join", null, null, null), default));
        var revokeId = (Guid)revokable.Value!.GetType().GetProperty("Id")!.GetValue(revokable.Value)!;
        var revokePlain = (string)revokable.Value!.GetType().GetProperty("Key")!.GetValue(revokable.Value)!;
        Assert.IsType<NoContentResult>(await admin.RevokeKey(revokeId, default));

        Assert.Contains(adminLog.Lines, l => l.StartsWith("Audit InviteKeyCreated actor=" + adminId));
        Assert.Contains(adminLog.Lines, l => l.StartsWith("Audit InviteKeyRevoked actor=" + adminId));
        Assert.Contains(authLog.Lines, l => l.StartsWith("Audit InviteKeyRedeemed"));
        Assert.DoesNotContain(adminLog.Lines.Concat(authLog.Lines), l => l.Contains(plain) || l.Contains(revokePlain));
    }

    [Fact]
    public async Task UserAdminActions_AreAudited_QuotaLogsOldAndNew()
    {
        var env = AuthControllerTests.CreateEnv();
        await env.Controller.Register(new RegisterRequest("a@example.com", Password));
        var adminId = (await env.UserManager.FindByEmailAsync("a@example.com"))!.Id;
        var key = (await env.Keys.CreateAsync(InviteKeyType.Join, null, null, 7, adminId)).Plain;
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, key));
        var userId = (await env.UserManager.FindByEmailAsync("b@example.com"))!.Id;
        var log = new ListLogger<AdminController>();
        var admin = new AdminController(env.UserManager, env.Db, new StorageQuotaService(env.Db, 100), new NoAccounts(), env.Keys, new Caller(adminId), log);

        await admin.SetQuota(userId, new SetQuotaRequest(500), default);
        await admin.Disable(userId);
        await admin.Enable(userId);
        await admin.DeleteUser(userId, default);

        Assert.Contains(log.Lines, l => l.Contains("QuotaChanged") && l.Contains("old=") && l.EndsWith("new=500"));
        Assert.Contains(log.Lines, l => l.StartsWith($"Audit UserDisabled actor={adminId} target={userId}"));
        Assert.Contains(log.Lines, l => l.StartsWith("Audit UserEnabled"));
        Assert.Contains(log.Lines, l => l.StartsWith("Audit UserDeleted"));
    }

    [Fact]
    public async Task ResetKeyUse_IsAudited_WithoutKeyMaterial()
    {
        var env = AuthControllerTests.CreateEnv();
        await env.Controller.Register(new RegisterRequest("a@example.com", Password));
        var user = (await env.UserManager.FindByEmailAsync("a@example.com"))!;
        var plain = (await env.Keys.CreateAsync(InviteKeyType.Reset, null, user.Id, 7, user.Id)).Plain;
        var log = new ListLogger<AuthController>();
        var auth = new AuthController(env.UserManager, env.Configuration, env.Db, env.Keys, log);

        Assert.IsType<NoContentResult>(await auth.Reset(new ResetRequest(plain, "N3wPassw0rd!")));

        Assert.Contains(log.Lines, l => l.StartsWith($"Audit ResetKeyUsed actor={user.Id} target={user.Id}"));
        Assert.DoesNotContain(log.Lines, l => l.Contains(plain));
    }
}
