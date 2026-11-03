using M3A.Domain.Entities;
using M3A.Domain.Enums;
using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;

namespace M3A.Repositories;

/// <summary>EF Core implementation of <see cref="IEventRepository"/>.</summary>
public sealed class EventRepository(M3ADbContext dbContext) : IEventRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Event>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Events
            .AsNoTracking()
            .Include(e => e.Venue)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Event?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Events
            .Include(e => e.Venue)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Events.AnyAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Event entity, CancellationToken cancellationToken = default)
    {
        await dbContext.Events.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddWithTicketsAsync(
        Event entity,
        IReadOnlyCollection<Ticket> tickets,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Events.AddAsync(entity, cancellationToken);
        await dbContext.Tickets.AddRangeAsync(tickets, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateWithTicketsAsync(
        Event entity,
        IReadOnlyCollection<Ticket> ticketsToIssue,
        int unsoldTicketsToRemove,
        CancellationToken cancellationToken = default)
    {
        if (unsoldTicketsToRemove > 0)
        {
            var unsold = await dbContext.Tickets
                .Where(ticket => ticket.EventId == entity.Id && ticket.Status == TicketStatus.Issued)
                .Take(unsoldTicketsToRemove)
                .ToListAsync(cancellationToken);

            if (unsold.Count < unsoldTicketsToRemove)
            {
                return false;
            }

            dbContext.Tickets.RemoveRange(unsold);
        }

        dbContext.Events.Update(entity);
        await dbContext.Tickets.AddRangeAsync(ticketsToIssue, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Event entity, CancellationToken cancellationToken = default)
    {
        dbContext.Events.Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        // Unsold tickets go with the event. Sold ones are left in place, so the Restrict
        // foreign key still refuses the delete if any exist.
        var unsold = await dbContext.Tickets
            .Where(ticket => ticket.EventId == id && ticket.Status == TicketStatus.Issued)
            .ToListAsync(cancellationToken);
        dbContext.Tickets.RemoveRange(unsold);

        dbContext.Events.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
