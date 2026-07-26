using Microsoft.AspNetCore.Identity;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Infrastructure.Security;

public class CustomLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name)
    {
        return name?.Normalize().ToUpperInvariant();
    }

    public string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var result = EmailNormalizer.Normalize(email);
        return result.IsSuccess ? result.Value.ToUpperInvariant() : email.Normalize().ToUpperInvariant();
    }
}
