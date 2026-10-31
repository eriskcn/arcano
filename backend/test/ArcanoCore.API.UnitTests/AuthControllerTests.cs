using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
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

    private static AuthController CreateController()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IUserStore<IdentityUser<Guid>>, InMemoryUserStore>();
        services.AddIdentityCore<IdentityUser<Guid>>().AddDefaultTokenProviders();

        var userManager = services.BuildServiceProvider().GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-key-that-is-at-least-32-bytes-long"
            })
            .Build();

        return new AuthController(userManager, configuration);
    }

    private sealed class InMemoryUserStore :
        IUserStore<IdentityUser<Guid>>,
        IUserPasswordStore<IdentityUser<Guid>>,
        IUserEmailStore<IdentityUser<Guid>>,
        IUserLockoutStore<IdentityUser<Guid>>
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

        public void Dispose()
        {
        }
    }
}
