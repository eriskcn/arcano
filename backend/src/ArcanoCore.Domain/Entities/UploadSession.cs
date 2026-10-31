using System;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.Entities;

// An in-progress chunked upload: the S3 multipart upload that holds the parts so far.
public class UploadSession : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public Guid? FolderId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string StorageUploadId { get; set; } = string.Empty;
}
