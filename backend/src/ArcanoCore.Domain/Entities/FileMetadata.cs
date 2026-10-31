using System;
using System.Collections.Generic;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.Entities;

public class FileMetadata : BaseEntity<Guid>, IAuditableEntity
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StorageKey { get; set; } = string.Empty;

    public bool IsStarred { get; set; }

    // Set when the file moves to the trash; the stored object is kept until it is purged.
    public DateTime? DeletedAtUtc { get; set; }

    // Ownership — FK only; nav-prop to IdentityUser lives in Infrastructure
    public Guid OwnerId { get; set; }

    // Optional parent folder
    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }

    // Shared links
    public ICollection<SharedLink> SharedLinks { get; set; } = [];
}
