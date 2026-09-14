using M3A.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace M3A.Repositories.Persistence.Configurations;

/// <summary>Table mapping for tickets.</summary>
public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Id).HasMaxLength(64);
        builder.Property(ticket => ticket.EventId).HasMaxLength(64).IsRequired();
        builder.Property(ticket => ticket.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Declared without a navigation property: nothing reads a Ticket's Event, and the
        // wire contract carries only EventId. EF still creates the index on the FK column.
        // Restrict matches Event -> Venue: an event with tickets sold cannot be deleted
        // out from under them.
        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(ticket => ticket.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
