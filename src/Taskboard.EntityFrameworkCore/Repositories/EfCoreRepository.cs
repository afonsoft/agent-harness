using Microsoft.EntityFrameworkCore;
using Taskboard.EntityFrameworkCore.Data;
using Taskboard.Repositories;

namespace Taskboard.EntityFrameworkCore.Repositories;

public class EfCoreRepository<T> : IRepository<T>
    where T : class
{
    private readonly TaskboardDbContext _context;

    public EfCoreRepository(TaskboardDbContext context)
    {
        _context = context;
    }

    public IQueryable<T> Query => _context.Set<T>().AsQueryable();

    public Task<T?> GetAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
        => _context.Set<T>().FindAsync(new object?[] { id }, cancellationToken).AsTask();

    public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default)
        => _context.Set<T>().ToListAsync(cancellationToken).ContinueWith(t => (IReadOnlyList<T>)t.Result, cancellationToken);

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => _context.Set<T>().AddAsync(entity, cancellationToken).AsTask();

    public Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        _context.Set<T>().Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        _context.Set<T>().Remove(entity);
        return Task.CompletedTask;
    }

    public void Untrack(T entity)
        => _context.Entry(entity).State = EntityState.Detached;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
