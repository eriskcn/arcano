using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Common.Interfaces;

public interface IBloomFilter<T>
{
    Task AddAsync(T item, CancellationToken cancellationToken = default);
    Task<bool> MightContainAsync(T item, CancellationToken cancellationToken = default);
}
