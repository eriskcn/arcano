using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArcanoCore.API.Authorization;

public static class AdminBootstrap
{
    // Upgrade path: an install with users but no admin. Promotes Auth:BootstrapAdminEmail, else the sole user; otherwise logs an error.
    public static async Task RunAsync(IApplicationDbContext db, UserManager<IdentityUser<Guid>> userManager, string? bootstrapEmail, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await db.UserProfiles.AnyAsync(p => p.IsAdmin, cancellationToken) || !await db.UserProfiles.AnyAsync(cancellationToken))
        {
            return;
        }

        IdentityUser<Guid>? user = null;
        if (!string.IsNullOrWhiteSpace(bootstrapEmail))
        {
            var email = EmailNormalizer.Normalize(bootstrapEmail);
            user = email.IsSuccess ? await userManager.FindByEmailAsync(email.Value) : null;
            if (user == null)
            {
                logger.LogWarning("Auth:BootstrapAdminEmail does not match any user; nobody was promoted.");
                return;
            }
            if (user.IsDisabled() || await userManager.IsLockedOutAsync(user))
            {
                logger.LogWarning("Auth:BootstrapAdminEmail user is disabled or locked out; nobody was promoted.");
                return;
            }
        }
        else if (userManager.Users.Take(2).ToList() is [var only]) // sync: runs once at startup
        {
            // v0.1.0 personal install: the lone user was always the owner.
            user = only;
            logger.LogWarning("No admin existed and {Email} is the only user; promoted to admin.", user.Email);
        }
        else
        {
            logger.LogError("Users exist but none is an admin. Set Auth:BootstrapAdminEmail to the admin's email and restart.");
            return;
        }

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        if (profile == null)
        {
            db.UserProfiles.Add(profile = new UserProfile { UserId = user.Id });
        }
        profile.IsAdmin = true;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Promoted {Email} to admin.", user.Email);
    }
}
