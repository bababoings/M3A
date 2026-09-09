using M3A.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace M3A.Repositories.Persistence;

/// <summary>
/// EF Core unit of work for the service. One <see cref="DbSet{TEntity}"/> per aggregate root.
/// </summary>
public class M3ADbContext(DbContextOptions<M3ADbContext> options) : DbContext(options)
{
    /// <summary>Reference resource. Replace with the real aggregate roots.</summary>
    public DbSet<Item> Items => Set<Item>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(M3ADbContext).Assembly);
    }
}
