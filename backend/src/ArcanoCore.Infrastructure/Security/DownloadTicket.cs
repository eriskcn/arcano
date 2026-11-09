using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace ArcanoCore.Infrastructure.Security;

// A short-lived JWT for one download URL, sent as ?dt= so the browser can save a file straight to disk.
public static class DownloadTicket
{
    public const string Scheme = "Download";
    public const string Audience = "ArcanoCoreDownload";
    public const string PathClaim = "dl";
    public const string QueryKey = "dt";
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    // For [Authorize] on the download actions that accept a ticket as well as the session token.
    public const string SessionOrTicket = JwtBearerDefaults.AuthenticationScheme + "," + Scheme;

    // Routing ignores case and a trailing slash, so the comparison does too.
    public static bool IsValidFor(ClaimsPrincipal ticket, HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && ticket.FindFirstValue(PathClaim)?.TrimEnd('/') is { Length: > 0 } path
        && string.Equals(path, (request.PathBase + request.Path).Value?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
