using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace ArcanoCore.API.Authorization;

// Counts wrong passwords per client IP + email, so a stranger can only block themselves, never the account owner.
// shortcut: in-memory, single API instance, lost on restart; use a shared store if the API ever runs on several instances.
public sealed class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, (int Count, DateTimeOffset Expires)> _failures = new();

    public static string Key(HttpContext? http, string email) => $"{http?.Connection.RemoteIpAddress}|{email}";

    public bool IsBlocked(string key)
    {
        lock (_failures)
        {
            return _failures.TryGetValue(key, out var f) && f.Expires > DateTimeOffset.UtcNow && f.Count >= MaxFailures;
        }
    }

    public void Fail(string key)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_failures)
        {
            // Expired entries are dropped here, so the table only holds what failed in the last window.
            foreach (var k in _failures.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList()) _failures.Remove(k);
            _failures[key] = (_failures.TryGetValue(key, out var f) ? f.Count + 1 : 1, now + Window);
        }
    }

    public void Clear(string key)
    {
        lock (_failures) _failures.Remove(key);
    }
}
