using M3A.Tests.Builders;

namespace M3A.Domain.Tests.Entities;

/// <summary>Covers the <c>Venue</c> entity shape.</summary>
public class VenueTests
{
    [Fact]
    public void Builder_ProducesEntityWithRequestedValues()
    {
        var venue = new VenueBuilder()
            .WithId("venue-42")
            .WithLocation("Estadio Azteca, Mexico City")
            .WithCapacity(87000)
            .Build();

        Assert.Equal("venue-42", venue.Id);
        Assert.Equal("Estadio Azteca, Mexico City", venue.Location);
        Assert.Equal(87000, venue.Capacity);
    }
}
