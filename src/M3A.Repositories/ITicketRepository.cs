using M3A.Domain.Entities;

namespace M3A.Repositories;

/// <summary>Ticket data access.</summary>
public interface ITicketRepository : IRepository<Ticket>
{
    /// <summary>Returns how many tickets have been issued for the given event.</summary>
    Task<int> CountForEventAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>Returns how many tickets for the given event have been sold (Purchased or Redeemed).</summary>
    Task<int> CountSoldForEventAsync(string eventId, CancellationToken cancellationToken = default);
}
