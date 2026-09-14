using M3A.Domain.Entities;
using M3A.Domain.Exceptions;

namespace M3A.Delegates;

/// <summary>
/// Business logic orchestration for <see cref="Venue"/>.
/// The API layer talks to this and never to a repository directly.
/// </summary>
public interface IVenueDelegate
{
    /// <summary>Returns every venue.</summary>
    Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the venue with the given id, or <c>null</c> when it does not exist.</summary>
    Task<Venue?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates a venue, assigning its identifier.</summary>
    /// <exception cref="BusinessRuleViolationException">Capacity is not greater than zero.</exception>
    Task<Venue> CreateAsync(string location, int capacity, CancellationToken cancellationToken = default);

    /// <summary>Replaces every mutable field of an existing venue.</summary>
    /// <exception cref="EntityNotFoundException">No venue has the given id.</exception>
    /// <exception cref="BusinessRuleViolationException">Capacity is not greater than zero.</exception>
    Task<Venue> UpdateAsync(string id, string location, int capacity, CancellationToken cancellationToken = default);

    /// <summary>Deletes a venue.</summary>
    /// <exception cref="EntityNotFoundException">No venue has the given id.</exception>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
