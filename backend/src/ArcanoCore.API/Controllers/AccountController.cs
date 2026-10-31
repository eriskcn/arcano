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

    public AccountController(
        IStorageQuotaService quotaService,
        IAccountService accountService,
        ICurrentUser currentUser,
        UserManager<IdentityUser<Guid>> userManager,
        IConfiguration configuration)
    {
        _quotaService = quotaService;
        _accountService = accountService;
        _currentUser = currentUser;
        _userManager = userManager;
        _configuration = configuration;
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

        if (!await _userManager.CheckPasswordAsync(user, request.CurrentPassword))
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

    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] DeleteAccountRequest? request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(_currentUser.UserId ?? "");
        if (user == null)
        {
            return Unauthorized(new Error("Account.Unauthorized", "A valid user ID is required."));
        }

        if (string.IsNullOrEmpty(request?.Password) || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return BadRequest(new Error("Account.PasswordIncorrect", "Password is incorrect."));
        }

        await _accountService.DeleteUserDataAsync(user.Id, cancellationToken);
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
