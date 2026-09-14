using M3A.Api.Dtos;
using M3A.Domain.Entities;

namespace M3A.Api.Extensions;

/// <summary>Maps Ticket to TicketDto.</summary>
public static class TicketMappings
{
    public static TicketDto ToDto(this Ticket ticket) => new(ticket.Id, ticket.EventId, ticket.Status);

    public static IReadOnlyList<TicketDto> ToDtos(this IEnumerable<Ticket> tickets) =>
        tickets.Select(ToDto).ToList();
}
