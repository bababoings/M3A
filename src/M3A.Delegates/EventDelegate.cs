using M3A.Domain.Entities;
using M3A.Domain.Exceptions;
using M3A.Domain.ValueObjects;
using M3A.Repositories;

namespace M3A.Delegates;

/// <summary>
/// Default <see cref="IEventDelegate"/>. Business rules live here, not in the routes
/// and not in the repository.
/// </summary>
public sealed class EventDelegate(
    IEventRepository eventRepository,
    IVenueRepository venueRepository,
    ITicketRepository ticketRepository) : IEventDelegate
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken cancellationToken = default) =>
        eventRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Event?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        eventRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<Event> CreateAsync(
        string name,
        DateTimeOffset dateTime,
        int eventCapacity,
        string venueId,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacityIsPositive(eventCapacity);
        EnsureDateTimeInFuture(dateTime);

        var venue = await venueRepository.GetByIdAsync(venueId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Venue), venueId);

        EnsureCapacityWithinVenueLimit(eventCapacity, venue.Capacity);

        var @event = new Event
        {
            Id = ResourceId.New(),
            Name = name,
            DateTime = dateTime,
            EventCapacity = eventCapacity,
            VenueId = venueId,
            Venue = venue
        };

        await eventRepository.AddAsync(@event, cancellationToken);
        return @event;
    }

    /// <inheritdoc />
    public async Task<Event> UpdateAsync(
        string id,
        string name,
        DateTimeOffset dateTime,
        int eventCapacity,
        string venueId,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacityIsPositive(eventCapacity);

        var existing = await eventRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Event), id);

        var venue = await venueRepository.GetByIdAsync(venueId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Venue), venueId);

        EnsureCapacityWithinVenueLimit(eventCapacity, venue.Capacity);

        existing.Name = name;
        existing.DateTime = dateTime;
        existing.EventCapacity = eventCapacity;
        existing.VenueId = venueId;
        existing.Venue = venue;

        await eventRepository.UpdateAsync(existing, cancellationToken);
        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        // The tickets -> events foreign key is Restrict, so the database would reject this
        // with a constraint violation. Refusing it here makes it the 422 it always was,
        // rather than letting a DbUpdateException escape as a 500.
        if (await ticketRepository.ExistsForEventAsync(id, cancellationToken))
        {
            throw new BusinessRuleViolationException(
                $"Event '{id}' cannot be deleted because tickets have been issued for it.");
        }

        if (!await eventRepository.DeleteAsync(id, cancellationToken))
        {
            throw new EntityNotFoundException(nameof(Event), id);
        }
    }

    private static void EnsureCapacityIsPositive(int capacity)
    {
        if (capacity <= 0)
        {
            throw new BusinessRuleViolationException(
                $"Event capacity must be greater than zero, but was {capacity}.");
        }
    }

    private static void EnsureCapacityWithinVenueLimit(int eventCapacity, int venueCapacity)
    {
        if (eventCapacity > venueCapacity)
        {
            throw new BusinessRuleViolationException(
                $"Event capacity ({eventCapacity}) cannot exceed venue capacity ({venueCapacity}).");
        }
    }

    private static void EnsureDateTimeInFuture(DateTimeOffset dateTime)
    {
        if (dateTime <= DateTimeOffset.UtcNow)
        {
            throw new BusinessRuleViolationException("Event date and time must be in the future.");
        }
    }
}
