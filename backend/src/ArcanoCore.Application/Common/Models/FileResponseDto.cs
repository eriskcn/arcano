using System;

namespace ArcanoCore.Application.Common.Models;

public record FileResponseDto(Guid Id, string FileName, string ContentType, long FileSize);
