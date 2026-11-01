using System;
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
    // Upgrade path: an install with users but no admin. Promotes the user named by Auth:BootstrapAdminEmail; never promotes on its own.
    public static async Task RunAsync(IApplicationDbContext db, UserManager<IdentityUser<Guid>> userManager, string? bootstrapEmail, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await db.UserProfiles.AnyAsync(p => p.IsAdmin, cancellationToken) || !await db.UserProfiles.AnyAsync(cancellationToken))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(bootstrapEmail))
        {
            logger.LogWarning("Users exist but none is an admin. Set Auth:BootstrapAdminEmail to the admin's email and restart.");
            return;
        }

        var email = EmailNormalizer.Normalize(bootstrapEmail);
        var user = email.IsSuccess ? await userManager.FindByEmailAsync(email.Value) : null;
        if (user == null)
        {
            logger.LogWarning("Auth:BootstrapAdminEmail does not match any user; nobody was promoted.");
            return;
        }

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
        if (profile == null)
        {
            db.UserProfiles.Add(profile = new UserProfile { UserId = user.Id });
        }
        profile.IsAdmin = true;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Promoted {Email} to admin (Auth:BootstrapAdminEmail).", user.Email);
    }
}
