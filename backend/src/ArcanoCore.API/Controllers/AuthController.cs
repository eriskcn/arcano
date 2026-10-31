using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace ArcanoCore.API.Controllers;

public class AuthController : ApiControllerBase
{
    // Hash of a random string; verified against when the email is unknown so login timing does not reveal existing accounts.
    private static readonly string DummyPasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(new IdentityUser<Guid>(), Guid.NewGuid().ToString());

    private const string SessionStartClaim = JwtTokens.SessionStartClaim;
    // Longest a login may keep renewing its token, so a stolen token cannot live forever.
    private static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(24);

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IConfiguration _configuration;

    public AuthController(
        UserManager<IdentityUser<Guid>> userManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request)
    {
        if (!_configuration.GetValue("Auth:AllowRegistration", true))
        {
            return StatusCode(403, new Error("Auth.RegistrationDisabled", "Sign-up is disabled on this server."));
        }

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

        // 2. Create User
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            UserName = normalizedEmail
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Same message for duplicates so sign-up does not reveal which emails exist.
            if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                return BadRequest(new Error("Auth.RegisterFailed", "Could not create account."));
            }

            var firstError = result.Errors.FirstOrDefault();
            var description = firstError?.Description ?? "User creation failed.";
            return BadRequest(new Error("Auth.RegisterFailed", description));
        }

        return Ok(new { Message = "User registered successfully." });
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

        // 2. Find User
        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user == null)
        {
            // Hash anyway so unknown emails take as long as wrong passwords.
            _userManager.PasswordHasher.VerifyHashedPassword(new IdentityUser<Guid>(), DummyPasswordHash, request.Password);
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // Locked-out accounts get the same response as a wrong password so lockout is not revealed.
        if (await _userManager.IsLockedOutAsync(user))
        {
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // 3. Check Password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        // 4. Generate JWT Token
        var token = JwtTokens.Create(_configuration, user, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
        if (user == null || await _userManager.IsLockedOutAsync(user))
        {
            return Unauthorized(new Error("Auth.SessionExpired", "Session expired."));
        }

        return Ok(new { Token = JwtTokens.Create(_configuration, user, sessionStart) });
    }
}

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
