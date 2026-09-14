namespace M3A.Api.Dtos;

/// <summary>Representation of an event returned by the API.</summary>
/// <param name="Id">Resource identifier.</param>
/// <param name="Name">Human readable title of the event.</param>
/// <param name="DateTime">When the event takes place.</param>
/// <param name="EventCapacity">Total capacity allocated for this event.</param>
/// <param name="VenueId">Identifier of the venue hosting this event.</param>
/// <param name="Venue">Details of the hosting venue, if loaded.</param>
public sealed record EventDto(
    string Id,
    string Name,
    DateTimeOffset DateTime,
    int EventCapacity,
    string VenueId,
    VenueDto? Venue);

/// <summary>Payload for <c>POST /events</c>.</summary>
/// <param name="Name">Human readable title of the event.</param>
/// <param name="DateTime">When the event takes place.</param>
/// <param name="EventCapacity">Total capacity allocated for this event.</param>
/// <param name="VenueId">Identifier of the venue hosting this event.</param>
public sealed record CreateEventDto(
    string Name,
    DateTimeOffset DateTime,
    int EventCapacity,
    string VenueId);

/// <summary>Payload for <c>PUT /events/{eventId}</c>.</summary>
/// <param name="Name">Human readable title of the event.</param>
/// <param name="DateTime">When the event takes place.</param>
/// <param name="EventCapacity">Total capacity allocated for this event.</param>
/// <param name="VenueId">Identifier of the venue hosting this event.</param>
public sealed record UpdateEventDto(
    string Name,
    DateTimeOffset DateTime,
    int EventCapacity,
    string VenueId);
