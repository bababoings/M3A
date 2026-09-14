using M3A.Domain.Entities;
using M3A.Domain.Enums;
using M3A.Domain.Exceptions;
using M3A.Domain.ValueObjects;
using M3A.Repositories;

namespace M3A.Delegates;

/// <summary>Ticket business logic. Status changes are checked here.</summary>
public sealed class TicketDelegate(ITicketRepository ticketRepository) : ITicketDelegate
{
    public Task<IReadOnlyList<Ticket>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ticketRepository.GetAllAsync(cancellationToken);

    public Task<Ticket?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        ticketRepository.GetByIdAsync(id, cancellationToken);

    public async Task<Ticket> CreateAsync(string eventId, CancellationToken cancellationToken = default)
    {
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
