using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Infrastructure.Security;

public class UserDirectory(ApplicationDbContext db, ILookupNormalizer normalizer) : IUserDirectory
{
    public async Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = normalizer.NormalizeEmail(email);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
        return user == null ? null : ToDirectoryUser(user);
    }

    public async Task<Dictionary<Guid, DirectoryUser>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToListAsync(cancellationToken);
        return users.ToDictionary(u => u.Id, ToDirectoryUser);
    }

    // Disabled = locked out until the far future (same rule as the admin API); brute-force lockouts are short.
    private static DirectoryUser ToDirectoryUser(IdentityUser<Guid> u) =>
        new(u.Id, u.Email ?? "", u.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50));
}
