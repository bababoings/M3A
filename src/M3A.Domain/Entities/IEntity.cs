namespace M3A.Domain.Entities;

/// <summary>
/// Marker contract for aggregate roots persisted with a string identifier.
/// </summary>
public interface IEntity
{
    /// <summary>Stable identifier matching <see cref="ValueObjects.ResourceId"/>.</summary>
    string Id { get; }
}
