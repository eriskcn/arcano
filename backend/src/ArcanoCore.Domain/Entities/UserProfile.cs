using System;
using System.ComponentModel.DataAnnotations;

namespace ArcanoCore.Domain.Entities;

// Group-mode data for an Identity user, keyed by the user id (the Identity user type is left unchanged).
public class UserProfile
{
    [Key]
    public Guid UserId { get; set; }
    public bool IsAdmin { get; set; }
    // null = use the server default (Storage:QuotaBytes).
    public long? QuotaBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
