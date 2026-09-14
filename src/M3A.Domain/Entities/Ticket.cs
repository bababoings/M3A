using M3A.Domain.Enums;

namespace M3A.Domain.Entities;

public class Ticket : IEntity
{
    public string Id { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public TicketStatus Status { get; set; } = TicketStatus.Issued;
}
