using System;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.Entities;

public class SharedLink : BaseEntity<Guid>, IAuditableEntity
{
    // Unique token in the URL, e.g. /share/{Token}
    public string Token { get; set; } = string.Empty;

    // Target: exactly one of these is set
    public Guid? FileMetadataId { get; set; }
    public FileMetadata? FileMetadata { get; set; }

    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }

    // Creator — FK only
    public Guid CreatedById { get; set; }

    // null = never expires
    public DateTime? ExpiresAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
