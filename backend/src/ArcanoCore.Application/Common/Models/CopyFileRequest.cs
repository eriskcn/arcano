using System;

namespace ArcanoCore.Application.Common.Models;

// Null FolderId copies the file to the root.
public record CopyFileRequest(Guid? FolderId);
