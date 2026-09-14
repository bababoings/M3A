namespace M3A.Api.Dtos;

/// <summary>Representation of a venue returned by the API.</summary>
/// <param name="Id">Resource identifier.</param>
/// <param name="Location">Where the venue is.</param>
/// <param name="Capacity">Total number of people the venue holds.</param>
public sealed record VenueDto(string Id, string Location, int Capacity);

/// <summary>Payload for <c>POST /venues</c>.</summary>
/// <param name="Location">Where the venue is.</param>
/// <param name="Capacity">Total number of people the venue holds.</param>
public sealed record CreateVenueDto(string Location, int Capacity);

/// <summary>Payload for <c>PUT /venues/{venueId}</c>.</summary>
/// <param name="Location">Where the venue is.</param>
/// <param name="Capacity">Total number of people the venue holds.</param>
public sealed record UpdateVenueDto(string Location, int Capacity);
