using M3A.Api.Dtos;
using M3A.Domain.Entities;

namespace M3A.Api.Extensions;

/// <summary>Entity to DTO mapping for <see cref="Event"/>.</summary>
public static class EventMappings
{
    /// <summary>Projects a single entity onto its wire representation.</summary>
    public static EventDto ToDto(this Event @event) =>
        new(@event.Id, @event.Name, @event.DateTime, @event.EventCapacity, @event.VenueId, @event.Venue?.ToDto());

    /// <summary>Projects a collection of entities onto their wire representation.</summary>
    public static IReadOnlyList<EventDto> ToDtos(this IEnumerable<Event> events) =>
        events.Select(ToDto).ToList();
}
