namespace M3A.Domain.Entities;

/// <summary>
/// An organized gathering or performance scheduled at a venue.
/// </summary>
public class Event : IEntity
{
    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <summary>Human readable title of the event.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the event takes place.</summary>
    public DateTimeOffset DateTime { get; set; }

    /// <summary>Total capacity allocated for this event. Must be positive and cannot exceed the venue's capacity.</summary>
    public int EventCapacity { get; set; }

    /// <summary>Identifier of the venue hosting this event.</summary>
    public string VenueId { get; set; } = string.Empty;

    /// <summary>The venue hosting this event.</summary>
    public Venue? Venue { get; set; }
}
