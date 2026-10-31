using System;
using System.Collections.Generic;

namespace ArcanoCore.Application.Common.Models;

public record UploadStartDto(string FileName, string ContentType, long FileSize, Guid? FolderId);

public record UploadStartedDto(Guid Id, long ChunkSize);

public record UploadStatusDto(long FileSize, long ChunkSize, IReadOnlyList<int> UploadedParts);
