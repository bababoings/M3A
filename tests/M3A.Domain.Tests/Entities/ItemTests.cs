using M3A.Tests.Builders;

namespace M3A.Domain.Tests.Entities;

/// <summary>
/// Placeholder for <c>Item</c> business rules. Add one test class per entity as
/// invariants are introduced.
/// </summary>
public class ItemTests
{
    [Fact]
    public void Builder_ProducesEntityWithRequestedValues()
    {
        var item = new ItemBuilder().WithId("item-42").WithName("Ticket").Build();

        Assert.Equal("item-42", item.Id);
        Assert.Equal("Ticket", item.Name);
    }
}
