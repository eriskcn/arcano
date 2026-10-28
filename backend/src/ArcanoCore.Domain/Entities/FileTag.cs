using System;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.Entities;

public class FileTag : BaseEntity<Guid>
{
    public string Name { get; set; } = string.Empty;

    // Join table key
    public Guid FileMetadataId { get; set; }
    public FileMetadata FileMetadata { get; set; } = null!;
}
