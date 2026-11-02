using System;
using Microsoft.AspNetCore.Identity;

namespace ArcanoCore.Infrastructure.Security;

public static class UserAccountState
{
    // A disabled user is locked out until the far future; a brute-force lockout lasts minutes.
    public static bool IsDisabled(IdentityUser<Guid> user) => user.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50);

    // A failed-login lockout must not invalidate issued tokens (else anyone could sign a user out); only a disabled user does.
    public static bool IsTokenValid(IdentityUser<Guid>? user, string? stamp) =>
        user != null && !string.IsNullOrEmpty(stamp) && stamp == user.SecurityStamp && !IsDisabled(user);
}
