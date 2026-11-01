using System;

namespace ArcanoCore.Domain.Entities;

// Read-only grant of one file or one folder subtree from OwnerId to RecipientId. Exactly one of FileId/FolderId is set.
public class UserShare
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid? FileId { get; set; }
    public Guid? FolderId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
