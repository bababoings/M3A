using M3A.Domain.Entities;
using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;

namespace M3A.Repositories;

/// <summary>EF Core implementation of <see cref="IVenueRepository"/>.</summary>
public sealed class VenueRepository(M3ADbContext dbContext) : IVenueRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Venues.AsNoTracking().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Venue?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Venues.FirstOrDefaultAsync(venue => venue.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Venues.AnyAsync(venue => venue.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Venue entity, CancellationToken cancellationToken = default)
    {
        await dbContext.Venues.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Venue entity, CancellationToken cancellationToken = default)
    {
        dbContext.Venues.Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Venues.FirstOrDefaultAsync(venue => venue.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.Venues.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
