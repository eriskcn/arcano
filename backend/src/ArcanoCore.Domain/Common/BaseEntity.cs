using System;

namespace ArcanoCore.Domain.Common;

public abstract class BaseEntity<TKey>
{
    public TKey Id { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}
