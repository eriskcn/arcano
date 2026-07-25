using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
