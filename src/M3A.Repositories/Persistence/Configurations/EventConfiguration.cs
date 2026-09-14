using M3A.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace M3A.Repositories.Persistence.Configurations;

/// <summary>Mapping for <see cref="Event"/>.</summary>
public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(64);
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.DateTime).IsRequired();
        builder.Property(e => e.EventCapacity).IsRequired();
        builder.Property(e => e.VenueId).HasMaxLength(64).IsRequired();

        builder.HasOne(e => e.Venue)
            .WithMany()
            .HasForeignKey(e => e.VenueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
