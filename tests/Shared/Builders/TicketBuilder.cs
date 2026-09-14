using M3A.Domain.Entities;
using M3A.Domain.Enums;

namespace M3A.Tests.Builders;

/// <summary>
/// Fluent builder for <see cref="Ticket"/> test data, so tests state only the fields
/// they care about. Linked into each test project.
/// </summary>
public sealed class TicketBuilder
{
    private string _id = "ticket-1";
    private string _eventId = "event-1";
    private TicketStatus _status = TicketStatus.Issued;

    /// <summary>Sets the identifier.</summary>
    public TicketBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    /// <summary>Sets the event this ticket was issued for.</summary>
    public TicketBuilder WithEventId(string eventId)
    {
        _eventId = eventId;
        return this;
    }

    /// <summary>Sets the current status.</summary>
    public TicketBuilder WithStatus(TicketStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>Materializes the entity.</summary>
    public Ticket Build() => new() { Id = _id, EventId = _eventId, Status = _status };
}
