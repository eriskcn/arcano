using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Authorization;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class AccountController : ApiControllerBase
{
    private readonly IStorageQuotaService _quotaService;
    private readonly IAccountService _accountService;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IApplicationDbContext _dbContext;
    private readonly LoginThrottle _throttle;

    public AccountController(
        IStorageQuotaService quotaService,
        IAccountService accountService,
        ICurrentUser currentUser,
        UserManager<IdentityUser<Guid>> userManager,
        IConfiguration configuration,
        IApplicationDbContext dbContext,
        LoginThrottle? throttle = null)
    {
        _throttle = throttle ?? new LoginThrottle();
        _quotaService = quotaService;
        _accountService = accountService;
        _currentUser = currentUser;
        _userManager = userManager;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    // Failures are counted per IP + email like sign-in (not per account), so a stranger cannot lock the owner out; a blocked caller gets the plain wrong-password response.
    private async Task<bool> VerifyPasswordAsync(IdentityUser<Guid> user, string? password)
    {
        var key = LoginThrottle.Key(HttpContext, user.Email ?? "");
        if (string.IsNullOrEmpty(password) || _throttle.IsBlocked(key) || await _userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        if (!await _userManager.CheckPasswordAsync(user, password))
        {
            _throttle.Fail(key);
            return false;
        }

        _throttle.Clear(key);
        return true;
    }

    [HttpGet("storage")]
    public async Task<IActionResult> Storage(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var ownerId) || ownerId == Guid.Empty)
        {
            return Unauthorized(new Error("Account.Unauthorized", "A valid user ID is required."));
        }

        return Ok(await _quotaService.GetUsageAsync(ownerId, cancellationToken));
    }

    // Changing the password rotates the security stamp (revoking all other sessions); the returned token keeps this one signed in.
    [EnableRateLimiting("auth")]
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            return BadRequest(new Error("Account.PasswordInvalid", "Current and new password are required."));
        }

        var user = await _userManager.FindByIdAsync(_currentUser.UserId ?? "");
        if (user == null)
        {
            return Unauthorized(new Error("Account.Unauthorized", "A valid user ID is required."));
        }

        if (!await VerifyPasswordAsync(user, request.CurrentPassword))
        {
            return BadRequest(new Error("Account.PasswordIncorrect", "Current password is incorrect."));
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new Error("Account.PasswordInvalid", result.Errors.FirstOrDefault()?.Description ?? "Password change failed."));
        }

        var sessionStart = long.TryParse(User.FindFirstValue(JwtTokens.SessionStartClaim), out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Ok(new { Token = JwtTokens.Create(_configuration, user, sessionStart, await _dbContext.IsAdminAsync(user.Id)) });
    }

    [EnableRateLimiting("auth")]
    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] DeleteAccountRequest? request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId ?? "");
        if (user == null)
        {
            return Unauthorized(new Error("Account.Unauthorized", "A valid user ID is required."));
        }

        if (!await VerifyPasswordAsync(user, request?.Password))
        {
            return BadRequest(new Error("Account.PasswordIncorrect", "Password is incorrect."));
        }

        // Same rule as AdminController, except a last admin who is the only user left may leave (nobody to lock out).
        if (await _dbContext.IsLastActiveAdminAsync(_userManager, user) && await _dbContext.UserProfiles.AnyAsync(p => p.UserId != user.Id, cancellationToken))
        {
            return Conflict(new Error("Admin.LastAdmin", "The last active admin cannot be deleted or disabled."));
        }

        // Data first: the owner foreign keys are Restrict, so the user row cannot go while files, folders or links remain.
        await _accountService.DeleteUserDataAsync(user.Id, cancellationToken);
        // Reload: the purge can take a while, and any update to the user row meanwhile (disable, sign out everywhere, password change) would make DeleteAsync fail on the stale concurrency stamp after the data is already gone.
        await _dbContext.ReloadAsync(user, cancellationToken);
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new Error("Account.DeleteFailed", "Could not delete the account. Please try again."));
        }

        return NoContent();
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record DeleteAccountRequest(string Password);
