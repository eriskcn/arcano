using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Authorization;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.API.Controllers;

// Admin-only user and key management. There is deliberately no endpoint that reads another user's files.
[Authorize(Policy = AdminPolicy.Name)]
public class AdminController : ApiControllerBase
{
    private const int DefaultKeyDays = 7;
    private const int MaxKeyDays = 30;

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IApplicationDbContext _dbContext;
    private readonly IStorageQuotaService _quotaService;
    private readonly IAccountService _accountService;
    private readonly IInviteKeyService _inviteKeys;
    private readonly ICurrentUser _currentUser;

    public AdminController(
        UserManager<IdentityUser<Guid>> userManager,
        IApplicationDbContext dbContext,
        IStorageQuotaService quotaService,
        IAccountService accountService,
        IInviteKeyService inviteKeys,
        ICurrentUser currentUser)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _quotaService = quotaService;
        _accountService = accountService;
        _inviteKeys = inviteKeys;
        _currentUser = currentUser;
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync(cancellationToken);
        var profiles = await _dbContext.UserProfiles.ToDictionaryAsync(p => p.UserId, cancellationToken);
        var result = new List<AdminUserDto>();
        foreach (var user in users)
        {
            profiles.TryGetValue(user.Id, out var profile);
            var usage = await _quotaService.GetUsageAsync(user.Id, cancellationToken);
            result.Add(new AdminUserDto(user.Id, user.Email, profile?.IsAdmin ?? false, user.IsDisabled(),
                usage.UsedBytes, profile?.QuotaBytes, usage.LimitBytes, profile?.CreatedAtUtc ?? default));
        }
        return Ok(result);
    }

    [HttpPut("users/{id:guid}/quota")]
    public async Task<IActionResult> SetQuota(Guid id, [FromBody] SetQuotaRequest? request, CancellationToken cancellationToken)
    {
        if (request == null || request.QuotaBytes < 0)
        {
            return BadRequest(new Error("Admin.InvalidInput", "quotaBytes must be null or zero or more."));
        }

        var profile = await _dbContext.UserProfiles.FirstOrDefaultAsync(p => p.UserId == id, cancellationToken);
        if (profile == null)
        {
            return NotFound(UserNotFound);
        }

        profile.QuotaBytes = request.QuotaBytes;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // Disabling is an Identity lockout until the far future plus a security-stamp rotation; token validation rejects locked users.
    [HttpPost("users/{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound(UserNotFound);
        }
        if (await _dbContext.IsLastActiveAdminAsync(_userManager, user))
        {
            return Conflict(LastAdmin);
        }

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("users/{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound(UserNotFound);
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        return NoContent();
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound(UserNotFound);
        }
        if (await _dbContext.IsLastActiveAdminAsync(_userManager, user))
        {
            return Conflict(LastAdmin);
        }

        // Data first (same order as AccountController.Delete): owner foreign keys are Restrict.
        await _accountService.DeleteUserDataAsync(user.Id, cancellationToken);
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new Error("Admin.DeleteFailed", "Could not delete the user. Please try again."));
        }
        return NoContent();
    }

    [HttpGet("keys")]
    public async Task<IActionResult> Keys(CancellationToken cancellationToken)
    {
        var result = new List<object>();
        foreach (var key in await _inviteKeys.ListOpenAsync(cancellationToken))
        {
            var email = key.TargetUserId is { } target ? (await _userManager.FindByIdAsync(target.ToString()))?.Email : null;
            result.Add(new { key.Id, Type = key.Type.ToString(), key.QuotaBytes, UserEmail = email, key.ExpiresAtUtc, key.CreatedAtUtc });
        }
        return Ok(result);
    }

    [HttpPost("keys")]
    public async Task<IActionResult> CreateKey([FromBody] CreateKeyRequest? request, CancellationToken cancellationToken)
    {
        var days = request?.ExpiresInDays ?? DefaultKeyDays;
        if (request == null || !Enum.TryParse<InviteKeyType>(request.Type, true, out var type) || !Enum.IsDefined(type)
            || days < 1 || days > MaxKeyDays || request.QuotaBytes < 0)
        {
            return BadRequest(new Error("Admin.InvalidInput", $"type must be Join or Reset, expiresInDays 1-{MaxKeyDays}, quotaBytes zero or more."));
        }
        if (type == InviteKeyType.Reset && (request.UserId is not { } uid || await _userManager.FindByIdAsync(uid.ToString()) == null))
        {
            return NotFound(UserNotFound);
        }
        if (!Guid.TryParse(_currentUser.UserId, out var adminId))
        {
            return Unauthorized(new Error("Admin.Unauthorized", "A valid user ID is required."));
        }

        var (key, plain) = await _inviteKeys.CreateAsync(type, request.QuotaBytes, request.UserId, days, adminId, cancellationToken);
        return StatusCode(201, new { key.Id, Key = plain, Type = key.Type.ToString(), key.ExpiresAtUtc });
    }

    [HttpDelete("keys/{id:guid}")]
    public async Task<IActionResult> RevokeKey(Guid id, CancellationToken cancellationToken) =>
        await _inviteKeys.RevokeAsync(id, cancellationToken) ? NoContent() : NotFound(new Error("Admin.KeyNotFound", "Key not found."));

    private static readonly Error UserNotFound = new("Admin.UserNotFound", "User not found.");
    // shortcut: check-then-write is not atomic, two concurrent requests could both pass; upgrade with a Serializable transaction if admins ever act concurrently.
    private static readonly Error LastAdmin = new("Admin.LastAdmin", "The last active admin cannot be deleted or disabled.");
}

public record AdminUserDto(Guid Id, string? Email, bool IsAdmin, bool IsDisabled, long UsedBytes, long? QuotaBytes, long EffectiveQuotaBytes, DateTime CreatedAtUtc);
public record SetQuotaRequest(long? QuotaBytes);
public record CreateKeyRequest(string? Type, long? QuotaBytes, Guid? UserId, int? ExpiresInDays);
