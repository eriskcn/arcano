using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ArcanoCore.API.Controllers;

public class AuthController : ApiControllerBase
{
    // Hash of a random string; verified against when the email is unknown so login timing does not reveal existing accounts.
    private static readonly string DummyPasswordHash = new PasswordHasher<IdentityUser<Guid>>().HashPassword(new IdentityUser<Guid>(), Guid.NewGuid().ToString());

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
        var token = GenerateJwtToken(user);

        return Ok(new { Token = token });
    }

    [Authorize]
    [HttpGet("session")]
    public IActionResult Session()
    {
        return Ok(new { Email = User.FindFirstValue(ClaimTypes.Email) });
    }

    private string GenerateJwtToken(IdentityUser<Guid> user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.UserName!)
        };

        var jwtSettings = _configuration.GetSection("Jwt");
        // Validated at startup in DependencyInjection.AddInfrastructure.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = double.TryParse(jwtSettings["ExpiryInMinutes"], out var parsedMinutes) ? parsedMinutes : 60;

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"] ?? "ArcanoCore",
            audience: jwtSettings["Audience"] ?? "ArcanoCoreUsers",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
