using System.IO;

namespace ArcanoCore.Application.Common.Models;

public record FileUploadDto(Stream Content, string FileName, string ContentType, long FileSize);
