using M3A.Domain.Entities;

namespace M3A.Repositories;

/// <summary>Ticket data access.</summary>
public interface ITicketRepository : IRepository<Ticket>
{
    /// <summary>Returns whether any ticket has been issued for the given event.</summary>
    Task<bool> ExistsForEventAsync(string eventId, CancellationToken cancellationToken = default);
}
