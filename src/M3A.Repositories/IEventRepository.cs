using M3A.Domain.Entities;

namespace M3A.Repositories;

/// <summary>Data access for the <see cref="Event"/> aggregate root.</summary>
public interface IEventRepository : IRepository<Event>
{
    /// <summary>
    /// Persists a new event together with the tickets issued for it, in a single save, so an
    /// event never exists without its full ticket inventory.
    /// </summary>
    Task AddWithTicketsAsync(
        Event entity,
        IReadOnlyCollection<Ticket> tickets,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to an existing event and resizes its unsold ticket inventory in a single
    /// save: <paramref name="ticketsToIssue"/> are added and up to
    /// <paramref name="unsoldTicketsToRemove"/> tickets still in <c>Issued</c> status are removed.
    /// Sold tickets are never touched.
    /// </summary>
    /// <returns>
    /// <c>false</c> (and nothing is saved) when fewer than <paramref name="unsoldTicketsToRemove"/>
    /// unsold tickets exist.
    /// </returns>
    Task<bool> UpdateWithTicketsAsync(
        Event entity,
        IReadOnlyCollection<Ticket> ticketsToIssue,
        int unsoldTicketsToRemove,
        CancellationToken cancellationToken = default);
}
