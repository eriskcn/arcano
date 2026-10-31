using System;

namespace ArcanoCore.Application.Common.Models;

public record FileListItemDto(Guid Id, string FileName, string ContentType, long FileSize, DateTime CreatedAtUtc, bool IsStarred, DateTime? LastModifiedAtUtc);
