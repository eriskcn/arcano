using System;
using BCryptNet = BCrypt.Net.BCrypt;

namespace ArcanoCore.Domain.Common;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty or whitespace.", nameof(password));
        }

        return BCryptNet.HashPassword(password);
    }

    public static bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty or whitespace.", nameof(password));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash cannot be empty or whitespace.", nameof(passwordHash));
        }

        try
        {
            return BCryptNet.Verify(password, passwordHash);
        }
        catch
        {
            return false;
        }
    }
}
