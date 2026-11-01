using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.API.Authorization;

public static class AdminPolicy
{
    public const string Name = "Admin";
    public const string Role = "Admin";
}

public class AdminRequirement : IAuthorizationRequirement
{
}

// Reads the profile on every request, so a demoted or deleted admin loses access at once; the token's role claim is only a UI hint.
// (A disabled user is already rejected by token validation, see AddInfrastructure.)
public class AdminHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly IApplicationDbContext _dbContext;

    public AdminHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await _dbContext.IsAdminAsync(id))
        {
            context.Succeed(requirement);
        }
    }
}

public static class AdminExtensions
{
    public static Task<bool> IsAdminAsync(this IApplicationDbContext db, Guid userId) =>
        db.UserProfiles.AnyAsync(p => p.UserId == userId && p.IsAdmin);

    // A disabled user is locked out until the far future; a brute-force lockout lasts minutes.
    public static bool IsDisabled(this IdentityUser<Guid> user) => user.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50);
}
