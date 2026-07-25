using System;

namespace ArcanoCore.Domain.Common;

public interface IAuditableEntity
{
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastModifiedAtUtc { get; set; }
}
