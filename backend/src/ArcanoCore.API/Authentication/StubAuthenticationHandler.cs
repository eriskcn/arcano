using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArcanoCore.API.Authentication;

public class StubAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public StubAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Extract Authorization header
        if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeaderValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization Header"));
        }

        var authorizationHeader = authorizationHeaderValues.ToString();
        if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();

        // Stub logic: If token exists, construct stub claims
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty Token"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "stub-user-123"),
            new Claim(ClaimTypes.Name, "StubUser"),
            new Claim(ClaimTypes.Role, "User")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
