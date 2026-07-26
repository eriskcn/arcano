using System;
using System.Net.Mail;

namespace ArcanoCore.Domain.Common;

public static class EmailNormalizer
{
    public static Result<string> Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result<string>.Failure(new Error("Email.Empty", "Email cannot be empty."));
        }

        try
        {
            var trimmed = email.Trim();
            if (trimmed.Contains(' '))
            {
                return Result<string>.Failure(new Error("Email.Invalid", "Email format is invalid."));
            }
            var parsed = new MailAddress(trimmed);
            var domain = parsed.Host.ToLowerInvariant();
            var local = parsed.User;

            if (domain is "gmail.com" or "googlemail.com")
            {
                domain = "gmail.com";
                local = RemoveTag(local).Replace(".", "");
            }
            else if (domain is "outlook.com" or "hotmail.com" or "live.com")
            {
                local = RemoveTag(local);
            }

            return Result<string>.Success($"{local.ToLowerInvariant()}@{domain}");
        }
        catch (FormatException)
        {
            return Result<string>.Failure(new Error("Email.Invalid", "Email format is invalid."));
        }
    }

    private static string RemoveTag(string local)
    {
        var idx = local.IndexOf('+');
        return idx >= 0 ? local[..idx] : local;
    }
}
