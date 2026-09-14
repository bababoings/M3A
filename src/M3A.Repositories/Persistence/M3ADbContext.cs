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

    /// <summary>Tickets table.</summary>
    public DbSet<Ticket> Tickets => Set<Ticket>();
    /// <summary>Venues an event can be held at.</summary>
    public DbSet<Venue> Venues => Set<Venue>();

    /// <summary>Events scheduled at venues.</summary>
    public DbSet<Event> Events => Set<Event>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(M3ADbContext).Assembly);
    }
}
