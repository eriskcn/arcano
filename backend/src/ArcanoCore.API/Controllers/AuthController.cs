using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ArcanoCore.API.Controllers;

public class AuthController : ApiControllerBase
{
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IBloomFilter<string> _emailBloomFilter;
    private readonly IConfiguration _configuration;

    public AuthController(
        UserManager<IdentityUser<Guid>> userManager,
        IBloomFilter<string> emailBloomFilter,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _emailBloomFilter = emailBloomFilter;
        _configuration = configuration;
    }

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

        // 2. Bloom Filter pre-check: if it MightContain, we must verify in DB.
        // If it doesn't contain, we can skip DB uniqueness check (though Identity will still check it).
        if (await _emailBloomFilter.MightContainAsync(normalizedEmail))
        {
            var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
            if (existingUser != null)
            {
                return BadRequest(new Error("Auth.EmailTaken", "Email is already in use."));
            }
        }

        // 3. Create User
        var user = new IdentityUser<Guid>
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            UserName = normalizedEmail
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var firstError = result.Errors.FirstOrDefault();
            var description = firstError?.Description ?? "User creation failed.";
            return BadRequest(new Error("Auth.RegisterFailed", description));
        }

        // 4. Update Bloom Filter
        await _emailBloomFilter.AddAsync(normalizedEmail);

        return Ok(new { Message = "User registered successfully." });
    }

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

        // 2. Bloom Filter check: if not in Bloom filter, user definitely does not exist
        if (!await _emailBloomFilter.MightContainAsync(normalizedEmail))
        {
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // 3. Find User
        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user == null)
        {
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // 4. Check Password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return Unauthorized(new Error("Auth.InvalidCredentials", "Invalid email or password."));
        }

        // 5. Generate JWT Token
        var token = GenerateJwtToken(user);

        return Ok(new { Token = token });
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
        var secret = jwtSettings["Secret"] ?? "SuperSecretKeyForArcanoCoreAPIAuthenticationMustBe32BytesLong!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
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
