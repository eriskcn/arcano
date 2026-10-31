using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Common.Interfaces;

// Chunked uploads: parts are stored as they arrive and joined into one object on completion.
public interface IMultipartStorage
{
    Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default);
    Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default);
    Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default);
    Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default);
}
