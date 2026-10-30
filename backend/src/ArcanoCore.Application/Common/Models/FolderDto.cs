using System;

namespace ArcanoCore.Application.Common.Models;

public record FolderDto(Guid Id, string Name, Guid? ParentId, DateTime CreatedAtUtc);

// Used for both create and update: update is a full replace of name and parent.
public record FolderRequestDto(string Name, Guid? ParentId);
