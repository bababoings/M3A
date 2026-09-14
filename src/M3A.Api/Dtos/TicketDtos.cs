using M3A.Domain.Enums;

namespace M3A.Api.Dtos;

/// <summary>Ticket returned by the API.</summary>
public sealed record TicketDto(string Id, string EventId, TicketStatus Status);

/// <summary>Request body to create a ticket.</summary>
public sealed record CreateTicketDto(string EventId);
