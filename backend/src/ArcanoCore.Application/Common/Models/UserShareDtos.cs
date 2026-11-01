using System;
using System.Collections.Generic;

namespace ArcanoCore.Application.Common.Models;

public record UserShareRequestDto(Guid? FileId, Guid? FolderId, string? Email);

public record UserShareDto(Guid Id, string Kind, Guid ItemId, string Name, string RecipientEmail, DateTime CreatedAtUtc);

public record UserShareRecipientDto(Guid Id, string RecipientEmail, DateTime CreatedAtUtc);

public record IncomingShareDto(Guid Id, string Kind, Guid ItemId, string Name, long? SizeBytes, string? ContentType, string OwnerEmail, DateTime CreatedAtUtc);

public record SharedFolderDto(Guid Id, string Name);

public record SharedFileDto(Guid Id, string FileName, long SizeBytes, string ContentType, DateTime CreatedAtUtc);

public record SharedChildrenDto(IReadOnlyList<SharedFolderDto> Folders, IReadOnlyList<SharedFileDto> Files);
