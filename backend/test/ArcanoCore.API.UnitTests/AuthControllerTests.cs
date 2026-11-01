using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Services;
using ArcanoCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class AuthControllerTests
{
    private const string Email = "user@example.com";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task Login_AfterRegister_WithCorrectPassword_ReturnsOkWithToken()
    {
        var controller = CreateController();
        Assert.IsType<OkObjectResult>(await controller.Register(new RegisterRequest(Email, Password)));

        var result = await controller.Login(new LoginRequest(Email, Password));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
        var token = ok.Value!.GetType().GetProperty("Token")!.GetValue(ok.Value) as string;
        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var controller = CreateController();
        await controller.Register(new RegisterRequest(Email, Password));

        var result = await controller.Login(new LoginRequest(Email, "Wrong-Passw0rd"));

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Login_AfterFiveWrongPasswords_RejectsCorrectPassword()
    {
        var controller = CreateController();
        await controller.Register(new RegisterRequest(Email, Password));

        for (var i = 0; i < 5; i++)
        {
            Assert.IsType<UnauthorizedObjectResult>(await controller.Login(new LoginRequest(Email, "Wrong-Passw0rd")));
        }

        Assert.IsType<UnauthorizedObjectResult>(await controller.Login(new LoginRequest(Email, Password)));
    }

    [Fact]
    public async Task Refresh_WithValidSession_ReturnsNewTokenWithSameSessionStart()
    {
        var (controller, userManager) = CreateControllerWithUserManager();
        await controller.Register(new RegisterRequest(Email, Password));
        var sessionStart = DateTimeOffset.UtcNow.AddHours(-23).ToUnixTimeSeconds();
        await SignInAs(controller, userManager, sessionStart.ToString());

        var result = await controller.Refresh();

        var ok = Assert.IsType<OkObjectResult>(result);
        var token = (string)ok.Value!.GetType().GetProperty("Token")!.GetValue(ok.Value)!;
        var claim = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(c => c.Type == "session_start");
        Assert.Equal(sessionStart.ToString(), claim.Value);
    }

    [Fact]
    public async Task Refresh_WithSessionOlderThan24Hours_ReturnsUnauthorized()
    {
        var (controller, userManager) = CreateControllerWithUserManager();
        await controller.Register(new RegisterRequest(Email, Password));
        await SignInAs(controller, userManager, DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeSeconds().ToString());

        Assert.IsType<UnauthorizedObjectResult>(await controller.Refresh());
    }

    [Fact]
    public async Task Refresh_WithoutSessionStartClaim_ReturnsUnauthorized()
    {
        var (controller, userManager) = CreateControllerWithUserManager();
        await controller.Register(new RegisterRequest(Email, Password));
        await SignInAs(controller, userManager, null);

        Assert.IsType<UnauthorizedObjectResult>(await controller.Refresh());
    }

    // Gives the controller a principal for the registered user, with the given session_start claim (omitted when null).
    private static async Task SignInAs(AuthController controller, UserManager<IdentityUser<Guid>> userManager, string? sessionStart)
    {
        var user = (await userManager.FindByEmailAsync(Email))!;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        if (sessionStart != null)
        {
            claims.Add(new Claim("session_start", sessionStart));
        }
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }
        };
    }

    [Fact]
    public async Task Login_Token_CarriesCurrentSecurityStamp()
    {
        var (controller, userManager) = CreateControllerWithUserManager();
        await controller.Register(new RegisterRequest(Email, Password));

        var ok = Assert.IsType<OkObjectResult>(await controller.Login(new LoginRequest(Email, Password)));
        var token = (string)ok.Value!.GetType().GetProperty("Token")!.GetValue(ok.Value)!;

        var claim = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(c => c.Type == "sstamp");
        Assert.Equal((await userManager.FindByEmailAsync(Email))!.SecurityStamp, claim.Value);
    }

    [Fact]
    public async Task Logout_RotatesSecurityStamp()
    {
        var (controller, userManager) = CreateControllerWithUserManager();
        await controller.Register(new RegisterRequest(Email, Password));
        var user = (await userManager.FindByEmailAsync(Email))!;
        var before = user.SecurityStamp;
        await SignInAs(controller, userManager, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        Assert.IsType<NoContentResult>(await controller.Logout());

        Assert.False(string.IsNullOrEmpty(before));
        Assert.NotEqual(before, (await userManager.FindByEmailAsync(Email))!.SecurityStamp);
    }

    private static AuthController CreateController() => CreateControllerWithUserManager().Controller;

    private static (AuthController Controller, UserManager<IdentityUser<Guid>> UserManager) CreateControllerWithUserManager()
    {
        var env = CreateEnv();
        return (env.Controller, env.UserManager);
    }

    internal sealed record Env(AuthController Controller, UserManager<IdentityUser<Guid>> UserManager, IConfiguration Configuration, ApplicationDbContext Db, InviteKeyService Keys);

    internal static Env CreateEnv()
    {
        var (userManager, configuration) = CreateUserManager();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var keys = new InviteKeyService(db);
        return new Env(new AuthController(userManager, configuration, db, keys), userManager, configuration, db, keys);
    }

    internal static (UserManager<IdentityUser<Guid>> UserManager, IConfiguration Configuration) CreateUserManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IUserStore<IdentityUser<Guid>>, InMemoryUserStore>();
        services.AddIdentityCore<IdentityUser<Guid>>(o => o.Password.RequiredLength = 8).AddDefaultTokenProviders();

        var userManager = services.BuildServiceProvider().GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-key-that-is-at-least-32-bytes-long"
            })
            .Build();

        return (userManager, configuration);
    }

    internal sealed class InMemoryUserStore :
        IUserStore<IdentityUser<Guid>>,
        IUserPasswordStore<IdentityUser<Guid>>,
        IUserEmailStore<IdentityUser<Guid>>,
        IUserLockoutStore<IdentityUser<Guid>>,
        IUserSecurityStampStore<IdentityUser<Guid>>
    {
        private readonly List<IdentityUser<Guid>> _users = new();

        public Task<IdentityResult> CreateAsync(IdentityUser<Guid> user, CancellationToken cancellationToken)
        {
            _users.Add(user);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(IdentityUser<Guid> user, CancellationToken cancellationToken)
        {
            _users.Remove(user);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<string> GetUserIdAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(IdentityUser<Guid> user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(IdentityUser<Guid> user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityUser<Guid>?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(_users.FirstOrDefault(u => u.Id.ToString() == userId));

        public Task<IdentityUser<Guid>?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(_users.FirstOrDefault(u => u.NormalizedUserName == normalizedUserName));

        public Task SetPasswordHashAsync(IdentityUser<Guid> user, string? passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash);

        public Task<bool> HasPasswordAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash != null);

        public Task SetEmailAsync(IdentityUser<Guid> user, string? email, CancellationToken cancellationToken)
        {
            user.Email = email;
            return Task.CompletedTask;
        }

        public Task<string?> GetEmailAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Email);

        public Task<bool> GetEmailConfirmedAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.EmailConfirmed);

        public Task SetEmailConfirmedAsync(IdentityUser<Guid> user, bool confirmed, CancellationToken cancellationToken)
        {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        public Task<IdentityUser<Guid>?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(_users.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail));

        public Task<string?> GetNormalizedEmailAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedEmail);

        public Task SetNormalizedEmailAsync(IdentityUser<Guid> user, string? normalizedEmail, CancellationToken cancellationToken)
        {
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.LockoutEnd);

        public Task SetLockoutEndDateAsync(IdentityUser<Guid> user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task<int> IncrementAccessFailedCountAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(++user.AccessFailedCount);

        public Task ResetAccessFailedCountAsync(IdentityUser<Guid> user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task<int> GetAccessFailedCountAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.AccessFailedCount);

        public Task<bool> GetLockoutEnabledAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.LockoutEnabled);

        public Task SetLockoutEnabledAsync(IdentityUser<Guid> user, bool enabled, CancellationToken cancellationToken)
        {
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task SetSecurityStampAsync(IdentityUser<Guid> user, string stamp, CancellationToken cancellationToken)
        {
            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecurityStampAsync(IdentityUser<Guid> user, CancellationToken cancellationToken) =>
            Task.FromResult(user.SecurityStamp);

        public void Dispose()
        {
        }
    }
}
