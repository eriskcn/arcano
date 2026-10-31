using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ArcanoCore.API.Controllers;

internal static class JwtTokens
{
    public const string SessionStartClaim = "session_start";

    public static string Create(IConfiguration configuration, IdentityUser<Guid> user, long sessionStart)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(SessionStartClaim, sessionStart.ToString(), ClaimValueTypes.Integer64),
            new Claim(AuthClaims.SecurityStamp, user.SecurityStamp ?? "")
        };

        var jwtSettings = configuration.GetSection("Jwt");
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
