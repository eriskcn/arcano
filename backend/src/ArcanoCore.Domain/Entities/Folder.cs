using System;
using System.Collections.Generic;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.Entities;

public class Folder : BaseEntity<Guid>, IAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsStarred { get; set; }

    // Ownership — FK only
    public Guid OwnerId { get; set; }

    // Self-referencing hierarchy
    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = [];

    // Contents
    public ICollection<FileMetadata> Files { get; set; } = [];
}
