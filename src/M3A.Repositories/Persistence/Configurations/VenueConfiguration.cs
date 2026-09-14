using M3A.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace M3A.Repositories.Persistence.Configurations;

/// <summary>Mapping for <see cref="Venue"/>.</summary>
public sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");
        builder.HasKey(venue => venue.Id);
        builder.Property(venue => venue.Id).HasMaxLength(64);
        builder.Property(venue => venue.Location).HasMaxLength(256).IsRequired();
        builder.Property(venue => venue.Capacity).IsRequired();
    }
}
