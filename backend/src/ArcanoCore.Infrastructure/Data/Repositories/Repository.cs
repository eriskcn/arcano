using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Infrastructure.Data.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly ApplicationDbContext DbContext;
    protected readonly DbSet<T> DbSet;

    public Repository(ApplicationDbContext dbContext)
    {
        DbContext = dbContext;
        DbSet = dbContext.Set<T>();
    }

    public async Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull
    {
        return await DbSet.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public IQueryable<T> Query()
    {
        return DbSet;
    }

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
    }

    public void Update(T entity)
    {
        DbSet.Update(entity);
    }

    public void Delete(T entity)
    {
        DbSet.Remove(entity);
    }
}
