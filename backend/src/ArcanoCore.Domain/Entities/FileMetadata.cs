using System;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.Entities;

public class FileMetadata : BaseEntity<Guid>, IAuditableEntity
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StorageKey { get; set; } = string.Empty;
}
