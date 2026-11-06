using System;
using System.Linq;
using System.Security.Claims;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArcanoCore.API.Controllers;

public class AuthController : ApiControllerBase
{
    // Hash of a random string; verified against when the email is unknown so login timing does not reveal existing accounts.
    private static readonly string DummyPasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(new IdentityUser<Guid>(), Guid.NewGuid().ToString());

    private const string SessionStartClaim = JwtTokens.SessionStartClaim;
    // Longest a login may keep renewing its token, so a stolen token cannot live forever.
    private static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(24);

    // shortcut: serializes sign-ups inside one process so two first sign-ups cannot both become admin; use a DB lock if the API ever runs on several instances.
    private static readonly SemaphoreSlim RegisterLock = new(1, 1);

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IApplicationDbContext _dbContext;
    private readonly IInviteKeyService _inviteKeys;
    private readonly ILogger<AuthController> _logger;
    private readonly LoginThrottle _throttle;

    public AuthController(
        UserManager<IdentityUser<Guid>> userManager,
        IConfiguration configuration,
        IApplicationDbContext dbContext,
        IInviteKeyService inviteKeys,
        ILogger<AuthController>? logger = null,
        LoginThrottle? throttle = null)
    {
        _throttle = throttle ?? new LoginThrottle();
        _logger = logger ?? NullLogger<AuthController>.Instance;
        _userManager = userManager;
        _configuration = configuration;
        _dbContext = dbContext;
        _inviteKeys = inviteKeys;
    }

    // Every account has a profile (the migration backfills existing ones), so "no profiles" means an empty install.
    [EnableRateLimiting("public-info")]
    [HttpGet("setup")]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken) =>
        Ok(new { RequiresKey = await _dbContext.UserProfiles.AnyAsync(cancellationToken) });

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new Error("Auth.InvalidInput", "Email and password are required."));
        }

        // 1. Normalize Email
        var normalizedResult = EmailNormalizer.Normalize(request.Email);
        if (normalizedResult.IsFailure)
        {
            return BadRequest(normalizedResult.Error);
        }
        var normalizedEmail = normalizedResult.Value;

        await RegisterLock.WaitAsync();
        try
        {
            // The first account needs no key and becomes admin; afterwards a Join key is required (and consumed, single use).
            var isFirst = !await _dbContext.UserProfiles.AnyAsync();
            InviteKey? invite = null;
            if (!isFirst)
            {
                var consumed = await _inviteKeys.ConsumeAsync(request.InviteKey, InviteKeyType.Join);
                if (consumed.IsFailure)
                {
                    return StatusCode(403, consumed.Error);
                }
                invite = consumed.Value;
            }

            // 2. Create User
            var user = new IdentityUser<Guid>
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                UserName = normalizedEmail
            };

            try
            {
                var result = await _userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    // The key was not used up by a failed sign-up.
                    if (invite != null)
                    {
                        await _inviteKeys.ReleaseAsync(invite.Id);
                    }

                    // Same message for duplicates so sign-up does not reveal which emails exist.
                    if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
                    {
                        return BadRequest(new Error("Auth.RegisterFailed", "Could not create account."));
                    }

                    var firstError = result.Errors.FirstOrDefault();
                    var description = firstError?.Description ?? "User creation failed.";
                    return BadRequest(new Error("Auth.RegisterFailed", description));
                }

                UserProfile? profile = null;
                try
                {
                    profile = new UserProfile { UserId = user.Id, IsAdmin = isFirst, QuotaBytes = invite?.QuotaBytes };
                    _dbContext.UserProfiles.Add(profile);
                    await _dbContext.SaveChangesAsync(CancellationToken.None);
                }
                catch
                {
                    _dbContext.UserProfiles.Remove(profile!); // else the key release below retries this failed insert
                    await _userManager.DeleteAsync(user); // no account without a profile
                    throw;
                }
            }
            catch when (invite != null)
            {
                await _inviteKeys.ReleaseAsync(invite.Id); // a crash must not use up the key
                throw;
            }

            if (invite != null)
            {
                _logger.LogInformation("Audit {Event} actor={ActorId} target={TargetId} type={KeyType} admin={KeyCreatorId}", "InviteKeyRedeemed", user.Id, invite.Id, invite.Type, invite.CreatedById);
            }
            var token = JwtTokens.Create(_configuration, user, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), isFirst);
            return Ok(new { Message = "User registered successfully.", Token = token });
        }
        finally
        {
            RegisterLock.Release();
        }
    }

    // Sets a new password with a Reset key. Changing the password rotates the security stamp, so every old token dies.
    [EnableRateLimiting("auth")]
    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetRequest? request)
    {
        if (request == null || string.IsNullOrEmpty(request.NewPassword))
        {
            return BadRequest(new Error("Auth.InvalidInput", "Key and new password are required."));
        }

        var consumed = await _inviteKeys.ConsumeAsync(request.Key, InviteKeyType.Reset);
        if (consumed.IsFailure)
        {
            return StatusCode(403, consumed.Error);
        }

        var user = consumed.Value.TargetUserId is { } id ? await _userManager.FindByIdAsync(id.ToString()) : null;
        if (user == null)
        {
            return StatusCode(403, new Error("Auth.InviteInvalid", "The key is invalid, expired or already used."));
        }

        try
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
            if (!result.Succeeded)
            {
                await _inviteKeys.ReleaseAsync(consumed.Value.Id);
                return BadRequest(new Error("Auth.PasswordInvalid", result.Errors.FirstOrDefault()?.Description ?? "Password reset failed."));
            }
        }
        catch
        {
            await _inviteKeys.ReleaseAsync(consumed.Value.Id); // a crash must not use up the key
            throw;
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        _logger.LogInformation("Audit {Event} actor={ActorId} target={TargetId} key={KeyId}", "ResetKeyUsed", user.Id, user.Id, consumed.Value.Id);
        return NoContent();
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new Error("Auth.InvalidInput", "Email and password are required."));
        }

        // 1. Normalize Email
        var normalizedResult = EmailNormalizer.Normalize(request.Email);
        if (normalizedResult.IsFailure)
        {
            return BadRequest(normalizedResult.Error);
        }
        var normalizedEmail = normalizedResult.Value;

        // Wrong passwords are counted per IP + email (not per account), so a stranger cannot lock the owner out.
        var throttleKey = LoginThrottle.Key(HttpContext, normalizedEmail);
        if (_throttle.IsBlocked(throttleKey))
        {
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // 2. Find User
        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user == null)
        {
            // Hash anyway so unknown emails take as long as wrong passwords.
            _userManager.PasswordHasher.VerifyHashedPassword(new IdentityUser<Guid>(), DummyPasswordHash, request.Password);
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // Disabled accounts (admin lockout) get the same response as a wrong password so it is not revealed.
        if (await _userManager.IsLockedOutAsync(user))
        {
            _userManager.PasswordHasher.VerifyHashedPassword(new IdentityUser<Guid>(), DummyPasswordHash, request.Password); // same timing as a wrong password
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // 3. Check Password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            _throttle.Fail(throttleKey);
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        _throttle.Clear(throttleKey);

        // 4. Generate JWT Token
        var token = JwtTokens.Create(_configuration, user, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), await _dbContext.IsAdminAsync(user.Id));

        return Ok(new { Token = token });
    }

    [Authorize]
    [HttpGet("session")]
    public IActionResult Session()
    {
        return Ok(new { Email = User.FindFirstValue(ClaimTypes.Email) });
    }

    // Rotates the user's SecurityStamp, which revokes every token issued to this user (all sessions), see JwtBearer OnTokenValidated.
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        if (user != null)
        {
            await _userManager.UpdateSecurityStampAsync(user);
        }

        return NoContent();
    }

    [Authorize]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!long.TryParse(User.FindFirstValue(SessionStartClaim), out var sessionStart) ||
            now - sessionStart > MaxSessionAge.TotalSeconds)
        {
            return Unauthorized(new Error("Auth.SessionExpired", "Session expired."));
        }

        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        if (user == null || user.IsDisabled())
        {
            return Unauthorized(new Error("Auth.SessionExpired", "Session expired."));
        }

        return Ok(new { Token = JwtTokens.Create(_configuration, user, sessionStart, await _dbContext.IsAdminAsync(user.Id)) });
    }
}

public record RegisterRequest(string Email, string Password, string? InviteKey = null);
public record ResetRequest(string Key, string NewPassword);
public record LoginRequest(string Email, string Password);
