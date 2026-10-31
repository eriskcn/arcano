using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ArcanoCore.API.Controllers;

[Authorize]
public class AccountController : ApiControllerBase
{
    private readonly IStorageQuotaService _quotaService;
    private readonly IAccountService _accountService;
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController>? _logger;

    public AccountController(
        IStorageQuotaService quotaService,
        IAccountService accountService,
        ICurrentUser currentUser,
        UserManager<IdentityUser<Guid>> userManager,
        IConfiguration configuration,
        ILogger<AccountController>? logger = null)
    {
        _quotaService = quotaService;
        _accountService = accountService;
        _currentUser = currentUser;
        _userManager = userManager;
        _configuration = configuration;
        _logger = logger;
    }

    // Counts failures toward lockout like sign-in; a locked account gets the plain wrong-password response so lockout is not revealed.
    private async Task<bool> VerifyPasswordAsync(IdentityUser<Guid> user, string? password)
    {
        if (string.IsNullOrEmpty(password) || await _userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        if (!await _userManager.CheckPasswordAsync(user, password))
        {
            await _userManager.AccessFailedAsync(user);
            return false;
        }

        await _userManager.ResetAccessFailedCountAsync(user);
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
        return Ok(new { Token = JwtTokens.Create(_configuration, user, sessionStart) });
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

        // User first: if this fails nothing is lost; S3 object deletion cannot be rolled back.
        var userId = user.Id;
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new Error("Account.DeleteFailed", "Could not delete the account. Please try again."));
        }

        try
        {
            await _accountService.DeleteUserDataAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            // shortcut: the account is already gone, so a data-deletion failure leaves orphan rows/objects that nothing removes; add a cleanup sweep for ownerless data.
            _logger?.LogError(ex, "Account {UserId} deleted but its data could not be fully removed.", userId);
        }

        return NoContent();
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record DeleteAccountRequest(string Password);
