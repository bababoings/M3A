using M3A.Tests.Builders;

namespace M3A.Domain.Tests.Entities;

/// <summary>Covers the <c>Event</c> entity shape.</summary>
public class EventTests
{
    [Fact]
    public void Builder_ProducesEntityWithRequestedValues()
    {
        var dt = DateTimeOffset.UtcNow.AddDays(14);
        var venue = new VenueBuilder()
            .WithId("venue-42")
            .WithLocation("Estadio Azteca, Mexico City")
            .WithCapacity(87000)
            .Build();

        var @event = new EventBuilder()
            .WithId("event-101")
            .WithName("Championship Final")
            .WithDateTime(dt)
            .WithEventCapacity(80000)
            .WithVenue(venue)
            .Build();

        Assert.Equal("event-101", @event.Id);
        Assert.Equal("Championship Final", @event.Name);
        Assert.Equal(dt, @event.DateTime);
        Assert.Equal(80000, @event.EventCapacity);
        Assert.Equal("venue-42", @event.VenueId);
        Assert.Same(venue, @event.Venue);
    }
}
