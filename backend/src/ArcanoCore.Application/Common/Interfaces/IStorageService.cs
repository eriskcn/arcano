using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Common.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
