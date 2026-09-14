using M3A.Domain.Entities;
using M3A.Tests.Builders;

namespace M3A.Repositories.Tests;

/// <summary>Repository suite for the <c>Event</c> aggregate root.</summary>
public class EventRepositoryTests : RepositoryTestBase
{
    private readonly IEventRepository _eventRepository;
    private readonly IVenueRepository _venueRepository;

    /// <summary>Builds the repository over a per-test in-memory database.</summary>
    public EventRepositoryTests()
    {
        _eventRepository = new EventRepository(DbContext);
        _venueRepository = new VenueRepository(DbContext);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenNoEvents() =>
        Assert.Empty(await _eventRepository.GetAllAsync(CancellationToken.None));

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsTheEntity_AndLoadsVenue()
    {
        var cancellationToken = CancellationToken.None;
        var venue = new VenueBuilder()
            .WithId("venue-1")
            .WithLocation("Foro Sol")
            .WithCapacity(65000)
            .Build();
        await _venueRepository.AddAsync(venue, cancellationToken);

        var dt = DateTimeOffset.UtcNow.AddDays(7);
        var @event = new EventBuilder()
            .WithId("event-1")
            .WithName("Rock Concert")
            .WithDateTime(dt)
            .WithEventCapacity(50000)
            .WithVenueId("venue-1")
            .Build();

        await _eventRepository.AddAsync(@event, cancellationToken);
        var found = await _eventRepository.GetByIdAsync("event-1", cancellationToken);

        Assert.NotNull(found);
        Assert.Equal("Rock Concert", found.Name);
        Assert.Equal(dt, found.DateTime);
        Assert.Equal(50000, found.EventCapacity);
        Assert.Equal("venue-1", found.VenueId);
        Assert.NotNull(found.Venue);
        Assert.Equal("Foro Sol", found.Venue.Location);
    }

    [Fact]
    public async Task ExistsAsync_ReflectsWhetherTheEventWasAdded()
    {
        var cancellationToken = CancellationToken.None;
        Assert.False(await _eventRepository.ExistsAsync("event-1", cancellationToken));

        var venue = new VenueBuilder().WithId("venue-1").Build();
        await _venueRepository.AddAsync(venue, cancellationToken);

        await _eventRepository.AddAsync(
            new EventBuilder().WithId("event-1").WithVenueId("venue-1").Build(),
            cancellationToken);

        Assert.True(await _eventRepository.ExistsAsync("event-1", cancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenIdIsUnknown() =>
        Assert.False(await _eventRepository.DeleteAsync("missing", CancellationToken.None));

    [Fact]
    public async Task DeleteAsync_RemovesEvent_WhenFound()
    {
        var cancellationToken = CancellationToken.None;
        var venue = new VenueBuilder().WithId("venue-1").Build();
        await _venueRepository.AddAsync(venue, cancellationToken);

        var @event = new EventBuilder().WithId("event-1").WithVenueId("venue-1").Build();
        await _eventRepository.AddAsync(@event, cancellationToken);

        Assert.True(await _eventRepository.DeleteAsync("event-1", cancellationToken));
        Assert.False(await _eventRepository.ExistsAsync("event-1", cancellationToken));
    }
}
