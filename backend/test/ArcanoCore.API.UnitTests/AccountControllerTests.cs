using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class AccountControllerTests
{
    private const string Email = "user@example.com";
    private const string Password = "Passw0rd!";

    [Fact]
    public async Task ChangePassword_WithCorrectCurrent_ReturnsFreshTokenAndRotatesStamp()
    {
        var (controller, userManager, _) = await Setup();
        var before = (await userManager.FindByEmailAsync(Email))!.SecurityStamp;

        var ok = Assert.IsType<OkObjectResult>(await controller.ChangePassword(new ChangePasswordRequest(Password, "N3w-Passw0rd")));

        var token = (string)ok.Value!.GetType().GetProperty("Token")!.GetValue(ok.Value)!;
        var user = (await userManager.FindByEmailAsync(Email))!;
        Assert.NotEqual(before, user.SecurityStamp);
        Assert.Equal(user.SecurityStamp, new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Single(c => c.Type == "sstamp").Value);
        Assert.True(await userManager.CheckPasswordAsync(user, "N3w-Passw0rd"));
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrent_ReturnsPasswordIncorrect()
    {
        var (controller, userManager, _) = await Setup();

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(new ChangePasswordRequest("Wrong-Passw0rd", "N3w-Passw0rd")));

        Assert.Equal("Account.PasswordIncorrect", Assert.IsType<Error>(bad.Value).Code);
        Assert.True(await userManager.CheckPasswordAsync((await userManager.FindByEmailAsync(Email))!, Password));
    }

    [Fact]
    public async Task ChangePassword_WithTooShortNew_ReturnsPasswordInvalid()
    {
        var (controller, _, _) = await Setup();

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ChangePassword(new ChangePasswordRequest(Password, "short")));

        Assert.Equal("Account.PasswordInvalid", Assert.IsType<Error>(bad.Value).Code);
    }

    [Fact]
    public async Task Delete_WithWrongPassword_DeletesNothing()
    {
        var (controller, userManager, accounts) = await Setup();

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Delete(new DeleteAccountRequest("Wrong-Passw0rd"), default));

        Assert.Equal("Account.PasswordIncorrect", Assert.IsType<Error>(bad.Value).Code);
        Assert.False(accounts.Deleted);
        Assert.NotNull(await userManager.FindByEmailAsync(Email));
    }

    [Fact]
    public async Task Delete_WithCorrectPassword_DeletesDataAndUser()
    {
        var (controller, userManager, accounts) = await Setup();

        Assert.IsType<NoContentResult>(await controller.Delete(new DeleteAccountRequest(Password), default));

        Assert.True(accounts.Deleted);
        Assert.Null(await userManager.FindByEmailAsync(Email));
    }

    [Fact]
    public async Task Delete_WithRepeatedWrongPassword_LocksAccountEvenForCorrectPassword()
    {
        var (controller, userManager, accounts) = await Setup();

        for (var i = 0; i < 5; i++)
        {
            Assert.IsType<BadRequestObjectResult>(await controller.Delete(new DeleteAccountRequest("Wrong-Passw0rd"), default));
        }

        var user = (await userManager.FindByEmailAsync(Email))!;
        Assert.True(await userManager.IsLockedOutAsync(user));
        Assert.IsType<BadRequestObjectResult>(await controller.Delete(new DeleteAccountRequest(Password), default));
        Assert.False(accounts.Deleted);
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrent_ResetsFailureCount()
    {
        var (controller, userManager, _) = await Setup();
        await controller.ChangePassword(new ChangePasswordRequest("Wrong-Passw0rd", "N3w-Passw0rd"));
        var user = (await userManager.FindByEmailAsync(Email))!;
        Assert.Equal(1, await userManager.GetAccessFailedCountAsync(user));

        Assert.IsType<OkObjectResult>(await controller.ChangePassword(new ChangePasswordRequest(Password, "N3w-Passw0rd")));

        Assert.Equal(0, await userManager.GetAccessFailedCountAsync(user));
    }

    private static async Task<(AccountController Controller, UserManager<IdentityUser<Guid>> UserManager, FakeAccountService Accounts)> Setup()
    {
        var (userManager, configuration) = AuthControllerTests.CreateUserManager();
        var auth = new AuthController(userManager, configuration);
        Assert.IsType<OkObjectResult>(await auth.Register(new RegisterRequest(Email, Password)));
        var user = (await userManager.FindByEmailAsync(Email))!;
        var accounts = new FakeAccountService();
        var controller = new AccountController(new FakeQuota(), accounts, new FakeCurrentUser(user.Id.ToString()), userManager, configuration)
        {
            // No session_start claim: ChangePassword falls back to "now".
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };
        return (controller, userManager, accounts);
    }

    private sealed class FakeAccountService : IAccountService
    {
        public bool Deleted { get; private set; }

        public Task DeleteUserDataAsync(Guid ownerId, CancellationToken cancellationToken = default)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUser(string id) : ICurrentUser
    {
        public string? UserId => id;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeQuota : IStorageQuotaService
    {
        public Task<StorageUsageDto> GetUsageAsync(Guid ownerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> EnsureWithinQuotaAsync(Guid ownerId, long additionalBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
