using M3A.Domain.Entities;
using M3A.Domain.Enums;
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

        // The whole inventory is issued up front, one ticket per place, and saved with the event.
        await eventRepository.AddWithTicketsAsync(
            @event, IssueTickets(@event.Id, eventCapacity), cancellationToken);
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

        // Sold tickets keep their places, so capacity can't drop below them.
        var sold = await ticketRepository.CountSoldForEventAsync(id, cancellationToken);
        if (eventCapacity < sold)
        {
            throw new BusinessRuleViolationException(
                $"Event capacity ({eventCapacity}) cannot be lower than the {sold} tickets already sold.");
        }

        // Grow or shrink the unsold pool so the inventory matches the new capacity.
        var issued = await ticketRepository.CountForEventAsync(id, cancellationToken);
        var difference = eventCapacity - issued;
        var ticketsToIssue = difference > 0 ? IssueTickets(id, difference) : [];
        var unsoldToRemove = difference < 0 ? -difference : 0;

        existing.Name = name;
        existing.DateTime = dateTime;
        existing.EventCapacity = eventCapacity;
        existing.VenueId = venueId;
        existing.Venue = venue;

        if (!await eventRepository.UpdateWithTicketsAsync(
                existing, ticketsToIssue, unsoldToRemove, cancellationToken))
        {
            // Tickets were sold between the count above and the resize.
            throw new BusinessRuleViolationException(
                $"Event capacity ({eventCapacity}) cannot be lower than the tickets already sold.");
        }

        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        // Unsold tickets are removed with the event; sold ones are not. The tickets -> events
        // foreign key is Restrict, so the database would reject the delete with a constraint
        // violation. Refusing it here makes it a 422 rather than a DbUpdateException 500.
        if (await ticketRepository.CountSoldForEventAsync(id, cancellationToken) > 0)
        {
            throw new BusinessRuleViolationException(
                $"Event '{id}' cannot be deleted because tickets have been sold for it.");
        }

        if (!await eventRepository.DeleteAsync(id, cancellationToken))
        {
            throw new EntityNotFoundException(nameof(Event), id);
        }
    }

    private static Ticket[] IssueTickets(string eventId, int count)
    {
        var tickets = new Ticket[count];
        for (var i = 0; i < count; i++)
        {
            tickets[i] = new Ticket { Id = ResourceId.New(), EventId = eventId, Status = TicketStatus.Issued };
        }

        return tickets;
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
