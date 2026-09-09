using M3A.Domain.Entities;

namespace M3A.Repositories;

/// <summary>
/// Data access contract shared by every aggregate root repository.
/// Implementations are swappable, which is what makes the delegate layer unit testable.
/// </summary>
/// <typeparam name="TEntity">The aggregate root.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class, IEntity
{
    /// <summary>Returns every entity.</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the entity with the given id, or <c>null</c>.</summary>
    Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Returns whether an entity with the given id exists.</summary>
    Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Persists a new entity.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing entity.</summary>
    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Removes an entity. Returns <c>false</c> when nothing was removed.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
