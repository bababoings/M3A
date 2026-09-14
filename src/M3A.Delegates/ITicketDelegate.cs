using M3A.Domain.Entities;
using M3A.Domain.Exceptions;

namespace M3A.Delegates;

/// <summary>Ticket business logic.</summary>
public interface ITicketDelegate
{
    Task<IReadOnlyList<Ticket>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Ticket?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates a ticket for the given event with status Issued.</summary>
    /// <exception cref="EntityNotFoundException">No event has the given id.</exception>
    Task<Ticket> CreateAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a ticket. Throws EntityNotFoundException if it doesn't exist.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Marks a ticket as Purchased. Only allowed if the ticket is Issued.</summary>
    Task<Ticket> PurchaseAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Marks a ticket as Redeemed. Only allowed if the ticket is Purchased.</summary>
    Task<Ticket> RedeemAsync(string id, CancellationToken cancellationToken = default);
}
