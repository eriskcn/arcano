using System;
using System.Collections.Generic;
using ArcanoCore.API.Authorization;
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

    public static string Create(IConfiguration configuration, IdentityUser<Guid> user, long sessionStart, bool isAdmin = false)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(SessionStartClaim, sessionStart.ToString(), ClaimValueTypes.Integer64),
            new Claim(AuthClaims.SecurityStamp, user.SecurityStamp ?? "")
        };
        // UI hint only; admin endpoints check the database (AdminHandler).
        if (isAdmin) claims.Add(new Claim("role", AdminPolicy.Role));

        var jwtSettings = configuration.GetSection("Jwt");
        var expiryMinutes = double.TryParse(jwtSettings["ExpiryInMinutes"], out var parsedMinutes) ? parsedMinutes : 60;
        return Write(configuration, jwtSettings["Audience"] ?? "ArcanoCoreUsers", claims, DateTime.UtcNow.AddMinutes(expiryMinutes));
    }

    // Copies the user id and security stamp of the (already validated) session, so signing out everywhere also revokes tickets.
    public static string CreateDownloadTicket(IConfiguration configuration, ClaimsPrincipal session, string path) =>
        Write(configuration, DownloadTicket.Audience,
            [
                new Claim(ClaimTypes.NameIdentifier, session.FindFirstValue(ClaimTypes.NameIdentifier) ?? ""),
                new Claim(AuthClaims.SecurityStamp, session.FindFirstValue(AuthClaims.SecurityStamp) ?? ""),
                new Claim(DownloadTicket.PathClaim, path)
            ],
            DateTime.UtcNow.Add(DownloadTicket.Lifetime));

    private static string Write(IConfiguration configuration, string audience, IEnumerable<Claim> claims, DateTime expires)
    {
        var jwtSettings = configuration.GetSection("Jwt");
        // Validated at startup in DependencyInjection.AddInfrastructure.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"] ?? "ArcanoCore",
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
