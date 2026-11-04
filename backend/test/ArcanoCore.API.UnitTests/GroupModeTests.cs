using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Authorization;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class GroupModeTests
{
    private const string Password = "Passw0rd!";

    private static string TokenOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (string)ok.Value!.GetType().GetProperty("Token")!.GetValue(ok.Value)!;
    }

    private static async Task<Guid> Register(AuthController auth, string email, string? key = null)
    {
        Assert.IsType<OkObjectResult>(await auth.Register(new RegisterRequest(email, Password, key)));
        return default;
    }

    private static async Task<IdentityUser<Guid>> User(AuthControllerTests.Env env, string email) =>
        (await env.UserManager.FindByEmailAsync(email))!;

    private static AdminController Admin(AuthControllerTests.Env env, Guid callerId, FakeAccounts? accounts = null) =>
        new(env.UserManager, env.Db, new StorageQuotaService(env.Db, 100), accounts ?? new FakeAccounts(), env.Keys, new FakeCurrentUser(callerId));

    private static async Task<string> NewKey(AuthControllerTests.Env env, InviteKeyType type = InviteKeyType.Join, long? quota = null, Guid? target = null) =>
        (await env.Keys.CreateAsync(type, quota, target, 7, Guid.NewGuid())).Plain;

    [Fact]
    public async Task Setup_RequiresKeyOnlyOnceAUserExists()
    {
        var env = AuthControllerTests.CreateEnv();

        Assert.False(RequiresKey(await env.Controller.Setup(default)));
        await Register(env.Controller, "a@example.com");
        Assert.True(RequiresKey(await env.Controller.Setup(default)));
    }

    private static bool RequiresKey(IActionResult r) =>
        (bool)Assert.IsType<OkObjectResult>(r).Value!.GetType().GetProperty("RequiresKey")!.GetValue(((OkObjectResult)r).Value)!;

    [Fact]
    public async Task FirstUser_NeedsNoKey_BecomesAdmin_AndTokenCarriesRole()
    {
        var env = AuthControllerTests.CreateEnv();

        var token = TokenOf(await env.Controller.Register(new RegisterRequest("a@example.com", Password, "ignored")));

        var user = await User(env, "a@example.com");
        Assert.True((await env.Db.UserProfiles.SingleAsync(p => p.UserId == user.Id)).IsAdmin);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims, c => c.Type == "role" && c.Value == "Admin");
    }

    [Fact]
    public async Task SecondUser_NeedsValidKey_KeyIsSingleUse_AndQuotaApplies()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");

        foreach (var key in new string?[] { null, "", "wrong" })
        {
            var denied = Assert.IsType<ObjectResult>(await env.Controller.Register(new RegisterRequest("b@example.com", Password, key)));
            Assert.Equal(403, denied.StatusCode);
            Assert.Equal("Auth.InviteInvalid", Assert.IsType<Error>(denied.Value).Code);
        }
        Assert.Null(await env.UserManager.FindByEmailAsync("b@example.com"));

        var good = await NewKey(env, quota: 123);
        var token = TokenOf(await env.Controller.Register(new RegisterRequest("b@example.com", Password, good)));
        var b = await User(env, "b@example.com");
        var profile = await env.Db.UserProfiles.SingleAsync(p => p.UserId == b.Id);
        Assert.False(profile.IsAdmin);
        Assert.Equal(123, profile.QuotaBytes);
        Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims, c => c.Type == "role");

        var reuse = Assert.IsType<ObjectResult>(await env.Controller.Register(new RegisterRequest("c@example.com", Password, good)));
        Assert.Equal(403, reuse.StatusCode);
    }

    [Fact]
    public async Task Register_WithWeakPassword_DoesNotBurnKey()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        var key = await NewKey(env);

        Assert.IsType<BadRequestObjectResult>(await env.Controller.Register(new RegisterRequest("b@example.com", "short", key)));

        Assert.IsType<OkObjectResult>(await env.Controller.Register(new RegisterRequest("b@example.com", Password, key)));
    }

    [Fact]
    public async Task Register_WithExpiredKey_IsRefused()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        var key = await NewKey(env);
        (await env.Db.InviteKeys.SingleAsync()).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await env.Db.SaveChangesAsync();

        Assert.Equal(403, Assert.IsType<ObjectResult>(await env.Controller.Register(new RegisterRequest("b@example.com", Password, key))).StatusCode);
    }

    [Fact]
    public async Task DisabledUser_CannotSignInOrRefresh_AndEnableRestoresAccess()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, await NewKey(env)));
        var a = await User(env, "a@example.com");
        var b = await User(env, "b@example.com");
        var admin = Admin(env, a.Id);

        Assert.IsType<NoContentResult>(await admin.Disable(b.Id));

        Assert.IsType<UnauthorizedObjectResult>(await env.Controller.Login(new LoginRequest("b@example.com", Password)));
        Assert.True(b.IsDisabled());
        // Token validation (AddInfrastructure) rejects disabled users; a refresh is refused the same way.
        env.Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, b.Id.ToString()), new Claim("session_start", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())], "t"))
            }
        };
        Assert.IsType<UnauthorizedObjectResult>(await env.Controller.Refresh());

        Assert.IsType<NoContentResult>(await admin.Enable(b.Id));
        Assert.IsType<OkObjectResult>(await env.Controller.Login(new LoginRequest("b@example.com", Password)));
    }

    [Fact]
    public async Task FailedLogins_KeepIssuedTokenValid_ButDisabledUserTokenIsRejected()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, await NewKey(env)));
        var a = await User(env, "a@example.com");
        var b = await User(env, "b@example.com");
        var stamp = b.SecurityStamp;

        for (var i = 0; i < 5; i++)
        {
            await env.Controller.Login(new LoginRequest("b@example.com", "Wrong-Passw0rd"));
        }
        Assert.False(await env.UserManager.IsLockedOutAsync(b)); // failures are throttled per IP + email, not locked on the account
        Assert.IsType<UnauthorizedObjectResult>(await env.Controller.Login(new LoginRequest("b@example.com", Password)));
        Assert.True(UserAccountState.IsTokenValid(b, stamp));

        Assert.IsType<NoContentResult>(await Admin(env, a.Id).Disable(b.Id));
        Assert.False(UserAccountState.IsTokenValid(b, stamp));
    }

    [Fact]
    public async Task LastAdmin_CannotBeDeletedOrDisabled_ButCanBeOnceAnotherAdminExists()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, await NewKey(env)));
        var a = await User(env, "a@example.com");
        var b = await User(env, "b@example.com");
        var accounts = new FakeAccounts();
        var admin = Admin(env, a.Id, accounts);

        Assert.Equal("Admin.LastAdmin", Assert.IsType<Error>(Assert.IsType<ConflictObjectResult>(await admin.Disable(a.Id)).Value).Code);
        Assert.IsType<ConflictObjectResult>(await admin.DeleteUser(a.Id, default));
        Assert.False(accounts.Deleted);
        Assert.NotNull(await env.UserManager.FindByIdAsync(a.Id.ToString()));

        // A normal member is not protected, and a second admin makes the first deletable.
        (await env.Db.UserProfiles.SingleAsync(p => p.UserId == b.Id)).IsAdmin = true;
        await env.Db.SaveChangesAsync();
        Assert.IsType<NoContentResult>(await admin.Disable(b.Id));
        Assert.IsType<ConflictObjectResult>(await admin.DeleteUser(a.Id, default)); // b is disabled, so a is the last active admin again
        Assert.IsType<NoContentResult>(await admin.Enable(b.Id));
        Assert.IsType<NoContentResult>(await admin.DeleteUser(a.Id, default));
        Assert.True(accounts.Deleted);
        Assert.Null(await env.UserManager.FindByIdAsync(a.Id.ToString()));
    }

    [Fact]
    public async Task ResetKey_SetsPassword_RotatesStamp_AndIsSingleUse()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        var a = await User(env, "a@example.com");
        var stamp = a.SecurityStamp;
        var key = await NewKey(env, InviteKeyType.Reset, target: a.Id);

        Assert.IsType<BadRequestObjectResult>(await env.Controller.Reset(new ResetRequest(key, "short")));
        Assert.IsType<NoContentResult>(await env.Controller.Reset(new ResetRequest(key, "N3w-Passw0rd")));

        Assert.NotEqual(stamp, (await User(env, "a@example.com")).SecurityStamp); // old tokens carry the old stamp and are rejected
        Assert.IsType<UnauthorizedObjectResult>(await env.Controller.Login(new LoginRequest("a@example.com", Password)));
        Assert.IsType<OkObjectResult>(await env.Controller.Login(new LoginRequest("a@example.com", "N3w-Passw0rd")));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await env.Controller.Reset(new ResetRequest(key, "An0ther-Passw0rd"))).StatusCode);
    }

    [Fact]
    public async Task Reset_WithJoinKeyOrGarbage_IsRefused()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        var join = await NewKey(env);

        Assert.Equal(403, Assert.IsType<ObjectResult>(await env.Controller.Reset(new ResetRequest(join, "N3w-Passw0rd"))).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await env.Controller.Reset(new ResetRequest("nope", "N3w-Passw0rd"))).StatusCode);
    }

    [Fact]
    public async Task CreateKey_ValidatesInput_AndReturnsPlainKeyOnce()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        var a = await User(env, "a@example.com");
        var admin = Admin(env, a.Id);

        Assert.IsType<BadRequestObjectResult>(await admin.CreateKey(new CreateKeyRequest("Join", null, null, 31), default));
        Assert.IsType<BadRequestObjectResult>(await admin.CreateKey(new CreateKeyRequest("Bogus", null, null, null), default));
        Assert.IsType<NotFoundObjectResult>(await admin.CreateKey(new CreateKeyRequest("Reset", null, null, null), default));

        var created = Assert.IsType<ObjectResult>(await admin.CreateKey(new CreateKeyRequest("Join", 5, null, null), default));
        Assert.Equal(201, created.StatusCode);
        var plain = (string)created.Value!.GetType().GetProperty("Key")!.GetValue(created.Value)!;
        Assert.True((await env.Keys.ConsumeAsync(plain, InviteKeyType.Join)).IsSuccess);
    }

    [Fact]
    public async Task AdminPolicy_ChecksDatabase_NotTheToken()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        var a = await User(env, "a@example.com");
        var handler = new AdminHandler(env.Db);
        // The token claims Admin either way; only the database decides.
        async Task<bool> Allowed()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, a.Id.ToString()), new Claim(ClaimTypes.Role, "Admin")], "t"));
            var ctx = new AuthorizationHandlerContext([new AdminRequirement()], principal, null);
            await handler.HandleAsync(ctx);
            return ctx.HasSucceeded;
        }

        Assert.True(await Allowed());
        (await env.Db.UserProfiles.SingleAsync()).IsAdmin = false;
        await env.Db.SaveChangesAsync();
        Assert.False(await Allowed());
    }

    [Fact]
    public async Task Bootstrap_PromotesNamedUser_OnlyWhenNoAdminExists()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, await NewKey(env)));
        var a = await User(env, "a@example.com");
        var b = await User(env, "b@example.com");
        (await env.Db.UserProfiles.SingleAsync(p => p.UserId == a.Id)).IsAdmin = false; // simulate an upgraded install
        await env.Db.SaveChangesAsync();

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, null, NullLogger.Instance);
        Assert.False(await env.Db.UserProfiles.AnyAsync(p => p.IsAdmin)); // several users and no email: never guesses

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, "B@Example.com", NullLogger.Instance);
        Assert.Equal([b.Id], await env.Db.UserProfiles.Where(p => p.IsAdmin).Select(p => p.UserId).ToListAsync());

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, "a@example.com", NullLogger.Instance);
        Assert.Equal([b.Id], await env.Db.UserProfiles.Where(p => p.IsAdmin).Select(p => p.UserId).ToListAsync());
    }

    private static async Task<(AuthControllerTests.Env Env, IdentityUser<Guid> A, IdentityUser<Guid> B)> TwoUsersNoAdmin()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, await NewKey(env)));
        env.Db.UserProfiles.ToList().ForEach(p => p.IsAdmin = false);
        await env.Db.SaveChangesAsync();
        return (env, await User(env, "a@example.com"), await User(env, "b@example.com"));
    }

    private static Task<bool> AnyAdmin(AuthControllerTests.Env env) => env.Db.UserProfiles.AnyAsync(p => p.IsAdmin);

    [Fact]
    public async Task Bootstrap_SoleUser_IsPromoted()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com");
        (await env.Db.UserProfiles.SingleAsync()).IsAdmin = false;
        await env.Db.SaveChangesAsync();

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, null, NullLogger.Instance);

        Assert.True(await AnyAdmin(env));
    }

    [Fact]
    public async Task Bootstrap_SeveralUsersNoEmail_LogsErrorAndPromotesNobody()
    {
        var (env, _, _) = await TwoUsersNoAdmin();
        var logger = new CapturingLogger();

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, null, logger);

        Assert.False(await AnyAdmin(env));
        Assert.Contains(Microsoft.Extensions.Logging.LogLevel.Error, logger.Levels);
    }

    [Fact]
    public async Task Bootstrap_DisabledTarget_IsNotPromoted()
    {
        var (env, _, b) = await TwoUsersNoAdmin();
        await env.UserManager.SetLockoutEnabledAsync(b, true);
        await env.UserManager.SetLockoutEndDateAsync(b, DateTimeOffset.MaxValue);

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, "b@example.com", NullLogger.Instance);

        Assert.False(await AnyAdmin(env));
    }

    [Fact]
    public async Task Bootstrap_ExistingAdmin_IsNeverChanged()
    {
        var env = AuthControllerTests.CreateEnv();
        await Register(env.Controller, "a@example.com"); // first user is admin
        await env.Controller.Register(new RegisterRequest("b@example.com", Password, await NewKey(env)));
        var b = await User(env, "b@example.com");

        await AdminBootstrap.RunAsync(env.Db, env.UserManager, "b@example.com", NullLogger.Instance);

        Assert.False(await env.Db.IsAdminAsync(b.Id));
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public System.Collections.Generic.List<Microsoft.Extensions.Logging.LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private sealed class FakeAccounts : IAccountService
    {
        public bool Deleted { get; private set; }
        public Task DeleteUserDataAsync(Guid ownerId, CancellationToken cancellationToken = default)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUser(Guid id) : ICurrentUser
    {
        public string? UserId => id.ToString();
        public bool IsAuthenticated => true;
    }
}
