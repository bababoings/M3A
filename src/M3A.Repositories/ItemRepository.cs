using M3A.Domain.Entities;
using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;

namespace M3A.Repositories;

/// <summary>EF Core implementation of <see cref="IItemRepository"/>.</summary>
public sealed class ItemRepository(M3ADbContext dbContext) : IItemRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Items.AsNoTracking().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Item?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Items.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Items.AnyAsync(item => item.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(Item entity, CancellationToken cancellationToken = default)
    {
        await dbContext.Items.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Item entity, CancellationToken cancellationToken = default)
    {
        dbContext.Items.Update(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Items.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.Items.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
