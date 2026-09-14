using M3A.Domain.Entities;

namespace M3A.Tests.Builders;

/// <summary>
/// Fluent builder for <see cref="Event"/> test data, so tests state only the fields
/// they care about. Linked into each test project.
/// </summary>
public sealed class EventBuilder
{
    private string _id = "event-1";
    private string _name = "Test Event";
    private DateTimeOffset _dateTime = DateTimeOffset.UtcNow.AddDays(7);
    private int _eventCapacity = 50;
    private string _venueId = "venue-1";
    private Venue? _venue;

    /// <summary>Sets the identifier.</summary>
    public EventBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    /// <summary>Sets the name.</summary>
    public EventBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the event date and time.</summary>
    public EventBuilder WithDateTime(DateTimeOffset dateTime)
    {
        _dateTime = dateTime;
        return this;
    }

    /// <summary>Sets the event capacity.</summary>
    public EventBuilder WithEventCapacity(int eventCapacity)
    {
        _eventCapacity = eventCapacity;
        return this;
    }

    /// <summary>Sets the venue identifier.</summary>
    public EventBuilder WithVenueId(string venueId)
    {
        _venueId = venueId;
        return this;
    }

    /// <summary>Sets the navigation venue.</summary>
    public EventBuilder WithVenue(Venue venue)
    {
        _venue = venue;
        _venueId = venue.Id;
        return this;
    }

    /// <summary>Materializes the entity.</summary>
    public Event Build() => new()
    {
        Id = _id,
        Name = _name,
        DateTime = _dateTime,
        EventCapacity = _eventCapacity,
        VenueId = _venueId,
        Venue = _venue
    };
}
