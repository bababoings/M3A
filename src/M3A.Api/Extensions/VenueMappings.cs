using M3A.Api.Dtos;
using M3A.Domain.Entities;

namespace M3A.Api.Extensions;

/// <summary>Entity to DTO mapping for <see cref="Venue"/>.</summary>
public static class VenueMappings
{
    /// <summary>Projects a single entity onto its wire representation.</summary>
    public static VenueDto ToDto(this Venue venue) => new(venue.Id, venue.Location, venue.Capacity);

    /// <summary>Projects a collection of entities onto their wire representation.</summary>
    public static IReadOnlyList<VenueDto> ToDtos(this IEnumerable<Venue> venues) =>
        venues.Select(ToDto).ToList();
}
