using M3A.Domain.Entities;
using M3A.Domain.Exceptions;
using M3A.Repositories;
using M3A.Tests.Builders;
using Moq;

namespace M3A.Delegates.Tests;

/// <summary>
/// Delegate suite for <c>Event</c>: repositories are mocked, so orchestration
/// and business rules are under test.
/// </summary>
public class EventDelegateTests
{
    private readonly Mock<IEventRepository> _eventRepoMock = new(MockBehavior.Strict);
    private readonly Mock<IVenueRepository> _venueRepoMock = new(MockBehavior.Strict);
    private readonly Mock<ITicketRepository> _ticketRepoMock = new(MockBehavior.Strict);
    private readonly IEventDelegate _delegate;

    public EventDelegateTests() =>
        _delegate = new EventDelegate(
            _eventRepoMock.Object, _venueRepoMock.Object, _ticketRepoMock.Object);

    /// <summary>Sets up the "no tickets issued" case that lets a delete through.</summary>
    private void NoTicketsFor(string eventId) =>
        _ticketRepoMock
            .Setup(repo => repo.ExistsForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        Assert.Null(await _delegate.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsEvent_WhenFound()
    {
        var existing = new EventBuilder().WithId("event-1").Build();
        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _delegate.GetByIdAsync("event-1");
        Assert.NotNull(result);
        Assert.Equal("event-1", result.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEvents()
    {
        var list = new List<Event> { new EventBuilder().Build() };
        _eventRepoMock
            .Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await _delegate.GetAllAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task CreateAsync_AssignsIdAndPersists_WhenValid()
    {
        var venue = new VenueBuilder().WithId("venue-1").WithCapacity(1000).Build();
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("venue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(venue);

        _eventRepoMock
            .Setup(repo => repo.AddAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var created = await _delegate.CreateAsync("Concert", dt, 500, "venue-1");

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Concert", created.Name);
        Assert.Equal(dt, created.DateTime);
        Assert.Equal(500, created.EventCapacity);
        Assert.Equal("venue-1", created.VenueId);
        _eventRepoMock.Verify(repo => repo.AddAsync(It.IsAny<Event>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task CreateAsync_Throws_WhenCapacityIsNotPositive(int capacity)
    {
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.CreateAsync("Concert", dt, capacity, "venue-1"));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenDateTimeIsNotInFuture()
    {
        var past = DateTimeOffset.UtcNow.AddMinutes(-5);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.CreateAsync("Concert", past, 100, "venue-1"));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenVenueNotFound()
    {
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("missing-venue", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Venue?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.CreateAsync("Concert", dt, 100, "missing-venue"));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenEventCapacityExceedsVenueCapacity()
    {
        var venue = new VenueBuilder().WithId("venue-1").WithCapacity(500).Build();
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("venue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(venue);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.CreateAsync("Concert", dt, 600, "venue-1"));
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenEventNotFound()
    {
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("missing-event", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Event?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.UpdateAsync("missing-event", "New Name", dt, 100, "venue-1"));
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenVenueNotFound()
    {
        var existing = new EventBuilder().WithId("event-1").Build();
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("missing-venue", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Venue?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.UpdateAsync("event-1", "New Name", dt, 100, "missing-venue"));
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenCapacityExceedsVenueCapacity()
    {
        var existing = new EventBuilder().WithId("event-1").Build();
        var venue = new VenueBuilder().WithId("venue-1").WithCapacity(300).Build();
        var dt = DateTimeOffset.UtcNow.AddDays(1);

        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("venue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(venue);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.UpdateAsync("event-1", "New Name", dt, 500, "venue-1"));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesFields_WhenValid()
    {
        var existing = new EventBuilder().WithId("event-1").WithName("Old").WithEventCapacity(100).Build();
        var venue = new VenueBuilder().WithId("venue-1").WithCapacity(1000).Build();
        var dt = DateTimeOffset.UtcNow.AddDays(2);

        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("venue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(venue);

        _eventRepoMock
            .Setup(repo => repo.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var updated = await _delegate.UpdateAsync("event-1", "New Name", dt, 400, "venue-1");

        Assert.Equal("New Name", updated.Name);
        Assert.Equal(dt, updated.DateTime);
        Assert.Equal(400, updated.EventCapacity);
        _eventRepoMock.Verify(repo => repo.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Throws_WhenNotFound()
    {
        NoTicketsFor("missing");
        _eventRepoMock
            .Setup(repo => repo.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _delegate.DeleteAsync("missing"));
    }

    [Fact]
    public async Task DeleteAsync_Succeeds_WhenFound()
    {
        NoTicketsFor("event-1");
        _eventRepoMock
            .Setup(repo => repo.DeleteAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _delegate.DeleteAsync("event-1");
        _eventRepoMock.Verify(repo => repo.DeleteAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Throws_WhenTicketsHaveBeenIssued()
    {
        _ticketRepoMock
            .Setup(repo => repo.ExistsForEventAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.DeleteAsync("event-1"));

        // Never reaches the database, where the foreign key would have produced a 500.
        _eventRepoMock.Verify(
            repo => repo.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
