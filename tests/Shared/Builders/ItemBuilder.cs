using M3A.Domain.Entities;

namespace M3A.Tests.Builders;

/// <summary>
/// Fluent builder for <see cref="Item"/> test data. Every entity gets one of these so
/// tests state only the fields they care about. Linked into each test project.
/// </summary>
public sealed class ItemBuilder
{
    private string _id = "item-1";
    private string _name = "Test Item";

    /// <summary>Sets the identifier.</summary>
    public ItemBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    /// <summary>Sets the name.</summary>
    public ItemBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Materializes the entity.</summary>
    public Item Build() => new() { Id = _id, Name = _name };
}
