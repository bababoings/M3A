using M3A.Domain.Entities;
using M3A.Domain.Exceptions;

namespace M3A.Delegates;

/// <summary>
/// Business logic orchestration for <see cref="Event"/>.
/// The API layer talks to this and never to a repository directly.
/// </summary>
public interface IEventDelegate
{
    /// <summary>Returns every event.</summary>
    Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the event with the given id, or <c>null</c> when it does not exist.</summary>
    Task<Event?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates an event, assigning its identifier.</summary>
    /// <exception cref="EntityNotFoundException">No venue has the given <paramref name="venueId"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">Capacity is not positive, capacity exceeds venue capacity, or date/time is not in the future.</exception>
    Task<Event> CreateAsync(
        string name,
        DateTimeOffset dateTime,
        int eventCapacity,
        string venueId,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces every mutable field of an existing event.</summary>
    /// <exception cref="EntityNotFoundException">No event has the given <paramref name="id"/> or no venue has the given <paramref name="venueId"/>.</exception>
    /// <exception cref="BusinessRuleViolationException">Capacity is not positive or capacity exceeds venue capacity.</exception>
    Task<Event> UpdateAsync(
        string id,
        string name,
        DateTimeOffset dateTime,
        int eventCapacity,
        string venueId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an event.</summary>
    /// <exception cref="EntityNotFoundException">No event has the given id.</exception>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
