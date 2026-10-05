namespace Taskboard.Repositories;

public interface IRepository<T>
    where T : class
{
    IQueryable<T> Query { get; }

    Task<T?> GetAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull;

    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches <paramref name="entity"/> from change tracking without touching
    /// the store. Used after an optimistic-concurrency loss: the stale tracked
    /// row would make every later <see cref="SaveChangesAsync"/> on the same
    /// scope fail again.
    /// </summary>
    void Untrack(T entity);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
