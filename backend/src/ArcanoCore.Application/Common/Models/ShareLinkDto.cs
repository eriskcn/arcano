using System;

namespace ArcanoCore.Application.Common.Models;

public record ShareLinkDto(Guid Id, Guid FileId, string Token, DateTime? ExpiresAtUtc, DateTime CreatedAtUtc);

public record ShareLinkRequestDto(Guid FileId, DateTime? ExpiresAtUtc);

public record SharedFileInfoDto(string FileName, string ContentType, long FileSize);
