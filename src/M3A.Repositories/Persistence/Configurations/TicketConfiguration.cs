using M3A.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace M3A.Repositories.Persistence.Configurations;

/// <summary>Table mapping for tickets. Add Fk for event id later.</summary>
public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Id).HasMaxLength(64);
        builder.Property(ticket => ticket.EventId).HasMaxLength(64).IsRequired();
        builder.Property(ticket => ticket.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(ticket => ticket.EventId);
    }
}
