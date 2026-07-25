using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArcanoCore.Application.Common.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull;
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    IQueryable<T> Query();
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Delete(T entity);
}
