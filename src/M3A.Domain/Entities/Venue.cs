namespace M3A.Domain.Entities;

/// <summary>
/// A place an event can be held at. Deliberately minimal for now — seating sections,
/// city/state and the owning event arrive with later slices of the domain.
/// </summary>
public class Venue : IEntity
{
    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <summary>Where the venue is, as free text (e.g. "Estadio Azteca, Mexico City").</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Total number of people the venue holds. Always greater than zero.</summary>
    public int Capacity { get; set; }
}
