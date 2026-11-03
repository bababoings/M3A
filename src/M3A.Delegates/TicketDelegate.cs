using M3A.Domain.Entities;
using M3A.Domain.Enums;
using M3A.Domain.Exceptions;
using M3A.Domain.ValueObjects;
using M3A.Repositories;

namespace M3A.Delegates;

/// <summary>Ticket business logic. Status changes are checked here.</summary>
public sealed class TicketDelegate(
    ITicketRepository ticketRepository,
    IEventRepository eventRepository) : ITicketDelegate
{
    public Task<IReadOnlyList<Ticket>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ticketRepository.GetAllAsync(cancellationToken);

    public Task<Ticket?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        ticketRepository.GetByIdAsync(id, cancellationToken);

    public async Task<Ticket> CreateAsync(string eventId, CancellationToken cancellationToken = default)
    {
        // Checked here so an unknown event is a 404 rather than the database's foreign key
        // violation surfacing as a 500.
        var @event = await eventRepository.GetByIdAsync(eventId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Event), eventId);

        // Every ticket, whatever its status, holds a place at the event.
        var issued = await ticketRepository.CountForEventAsync(eventId, cancellationToken);
        if (issued >= @event.EventCapacity)
        {
            throw new BusinessRuleViolationException(
                $"Event '{eventId}' is sold out: all {@event.EventCapacity} tickets have been issued.");
        }

        var ticket = new Ticket { Id = ResourceId.New(), EventId = eventId, Status = TicketStatus.Issued };
        await ticketRepository.AddAsync(ticket, cancellationToken);
        return ticket;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!await ticketRepository.DeleteAsync(id, cancellationToken))
        {
            throw new EntityNotFoundException(nameof(Ticket), id);
        }
    }

    public Task<Ticket> PurchaseAsync(string id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, from: TicketStatus.Issued, to: TicketStatus.Purchased, cancellationToken);

    public Task<Ticket> RedeemAsync(string id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, from: TicketStatus.Purchased, to: TicketStatus.Redeemed, cancellationToken);

    private async Task<Ticket> TransitionAsync(
        string id,
        TicketStatus from,
        TicketStatus to,
        CancellationToken cancellationToken)
    {
        var ticket = await ticketRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Ticket), id);

        if (ticket.Status != from)
        {
            throw new BusinessRuleViolationException(
                $"Ticket '{id}' cannot move to {to} because it is {ticket.Status}, not {from}.");
        }

        ticket.Status = to;
        await ticketRepository.UpdateAsync(ticket, cancellationToken);
        return ticket;
    }
}
