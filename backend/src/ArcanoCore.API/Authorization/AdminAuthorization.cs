using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Infrastructure.Security;
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

    public static bool IsDisabled(this IdentityUser<Guid> user) => UserAccountState.IsDisabled(user);

    // True when the user is an active admin and no other active admin exists.
    public static async Task<bool> IsLastActiveAdminAsync(this IApplicationDbContext db, UserManager<IdentityUser<Guid>> userManager, IdentityUser<Guid> user)
    {
        var adminIds = await db.UserProfiles.Where(p => p.IsAdmin).Select(p => p.UserId).ToListAsync();
        if (!adminIds.Contains(user.Id) || user.IsDisabled())
        {
            return false;
        }
        foreach (var other in adminIds.Where(i => i != user.Id))
        {
            var u = await userManager.FindByIdAsync(other.ToString());
            if (u != null && !u.IsDisabled())
            {
                return false;
            }
        }
        return true;
    }
}
