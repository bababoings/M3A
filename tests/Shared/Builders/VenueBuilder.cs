using M3A.Domain.Entities;

namespace M3A.Tests.Builders;

/// <summary>
/// Fluent builder for <see cref="Venue"/> test data, so tests state only the fields
/// they care about. Linked into each test project.
/// </summary>
public sealed class VenueBuilder
{
    private string _id = "venue-1";
    private string _location = "Test Location";
    private int _capacity = 100;

    /// <summary>Sets the identifier.</summary>
    public VenueBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    /// <summary>Sets the location.</summary>
    public VenueBuilder WithLocation(string location)
    {
        _location = location;
        return this;
    }

    /// <summary>Sets the capacity.</summary>
    public VenueBuilder WithCapacity(int capacity)
    {
        _capacity = capacity;
        return this;
    }

    /// <summary>Materializes the entity.</summary>
    public Venue Build() => new() { Id = _id, Location = _location, Capacity = _capacity };
}
