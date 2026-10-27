namespace M3A.Domain.Entities;

/// <summary>
/// Reference entity used as the template for every resource in this service.
/// Replace or duplicate this type when the real domain is introduced.
/// Do not include this or any reference to it in any form of migration or production code. It is only used to demonstrate the structure of the service and its tests.
/// </summary>
public class Item : IEntity
{
    /// <inheritdoc />
    public string Id { get; set; } = string.Empty;

    /// <summary>Human readable name of the item.</summary>
    public string Name { get; set; } = string.Empty;
}
