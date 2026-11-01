using System;

namespace ArcanoCore.Domain.Entities;

public enum InviteKeyType
{
    Join,
    Reset
}

// A single-use key. Only the SHA-256 hash of the key is stored; the plain key is shown once at creation.
public class InviteKey
{
    public Guid Id { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public InviteKeyType Type { get; set; }
    // Join: storage quota for the new member (null = server default).
    public long? QuotaBytes { get; set; }
    // Reset: the user whose password the key resets.
    public Guid? TargetUserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
