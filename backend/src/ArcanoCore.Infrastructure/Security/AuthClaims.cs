namespace ArcanoCore.Infrastructure.Security;

public static class AuthClaims
{
    // Identity SecurityStamp at issue time; a token is only valid while it matches the user's current stamp.
    public const string SecurityStamp = "sstamp";
}
