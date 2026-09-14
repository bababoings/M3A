using M3A.Domain.Entities;
using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;

namespace M3A.Repositories;

/// <summary>Ticket repository using EF Core.</summary>
public sealed class TicketRepository(M3ADbContext dbContext) : ITicketRepository
{
    public async Task<IReadOnlyList<Ticket>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Tickets.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Ticket?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Tickets.AnyAsync(ticket => ticket.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsForEventAsync(string eventId, CancellationToken cancellationToken = default) =>
        dbContext.Tickets.AnyAsync(ticket => ticket.EventId == eventId, cancellationToken);

    public async Task AddAsync(Ticket entity, CancellationToken cancellationToken = default)
    {
        await dbContext.Tickets.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Ticket entity, CancellationToken cancellationToken = default)
    {
        dbContext.Tickets.Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.Tickets.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
