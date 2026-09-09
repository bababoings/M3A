using M3A.Domain.Entities;
using M3A.Domain.Exceptions;

namespace M3A.Delegates;

/// <summary>
/// Business logic orchestration for <see cref="Item"/>.
/// The API layer talks to this and never to a repository directly.
/// </summary>
public interface IItemDelegate
{
    /// <summary>Returns every item.</summary>
    Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the item with the given id, or <c>null</c> when it does not exist.</summary>
    Task<Item?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates an item, assigning its identifier.</summary>
    /// <exception cref="BusinessRuleViolationException">A conflicting item already exists.</exception>
    Task<Item> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Replaces every mutable field of an existing item.</summary>
    /// <exception cref="EntityNotFoundException">No item has the given id.</exception>
    Task<Item> UpdateAsync(string id, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes an item.</summary>
    /// <exception cref="EntityNotFoundException">No item has the given id.</exception>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
