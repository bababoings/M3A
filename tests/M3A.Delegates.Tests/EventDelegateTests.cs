using M3A.Domain.Entities;
using M3A.Domain.Enums;
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
    private readonly EventDelegate _delegate;

    public EventDelegateTests() =>
        _delegate = new EventDelegate(
            _eventRepoMock.Object, _venueRepoMock.Object, _ticketRepoMock.Object);

    /// <summary>Sets up how many tickets exist for the event, whatever their status.</summary>
    private void TicketsIssuedFor(string eventId, int issued) =>
        _ticketRepoMock
            .Setup(repo => repo.CountForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(issued);

    /// <summary>Sets up how many tickets have been sold, which bounds how low capacity can go.</summary>
    private void TicketsSoldFor(string eventId, int sold) =>
        _ticketRepoMock
            .Setup(repo => repo.CountSoldForEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sold);

    /// <summary>Sets up the resize call, capturing what it was asked to do.</summary>
    private void CaptureUpdate(
        Event existing,
        Action<IReadOnlyCollection<Ticket>, int> capture,
        bool succeeds = true) =>
        _eventRepoMock
            .Setup(repo => repo.UpdateWithTicketsAsync(
                existing,
                It.IsAny<IReadOnlyCollection<Ticket>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<Event, IReadOnlyCollection<Ticket>, int, CancellationToken>(
                (_, toIssue, toRemove, _) => capture(toIssue, toRemove))
            .ReturnsAsync(succeeds);

    /// <summary>Sets up the existing event and its venue for an update.</summary>
    private Event ExistingEventAtVenue(int eventCapacity, int venueCapacity = 1000)
    {
        var existing = new EventBuilder().WithId("event-1").WithName("Old").WithEventCapacity(eventCapacity).Build();
        var venue = new VenueBuilder().WithId("venue-1").WithCapacity(venueCapacity).Build();
        _eventRepoMock
            .Setup(repo => repo.GetByIdAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _venueRepoMock
            .Setup(repo => repo.GetByIdAsync("venue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(venue);
        return existing;
    }

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

        IReadOnlyCollection<Ticket>? issued = null;
        _eventRepoMock
            .Setup(repo => repo.AddWithTicketsAsync(
                It.IsAny<Event>(), It.IsAny<IReadOnlyCollection<Ticket>>(), It.IsAny<CancellationToken>()))
            .Callback<Event, IReadOnlyCollection<Ticket>, CancellationToken>((_, tickets, _) => issued = tickets)
            .Returns(Task.CompletedTask);

        var created = await _delegate.CreateAsync("Concert", dt, 500, "venue-1");

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Concert", created.Name);
        Assert.Equal(dt, created.DateTime);
        Assert.Equal(500, created.EventCapacity);
        Assert.Equal("venue-1", created.VenueId);
        _eventRepoMock.Verify(
            repo => repo.AddWithTicketsAsync(
                It.IsAny<Event>(), It.IsAny<IReadOnlyCollection<Ticket>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // One Issued ticket per place, all for this event, each with its own id.
        Assert.NotNull(issued);
        Assert.Equal(500, issued.Count);
        Assert.All(issued, ticket =>
        {
            Assert.Equal(created.Id, ticket.EventId);
            Assert.Equal(TicketStatus.Issued, ticket.Status);
        });
        Assert.Equal(500, issued.Select(ticket => ticket.Id).Distinct().Count());
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
        var existing = ExistingEventAtVenue(eventCapacity: 400);
        var dt = DateTimeOffset.UtcNow.AddDays(2);
        TicketsSoldFor("event-1", 0);
        TicketsIssuedFor("event-1", 400);
        CaptureUpdate(existing, (_, _) => { });

        var updated = await _delegate.UpdateAsync("event-1", "New Name", dt, 400, "venue-1");

        Assert.Equal("New Name", updated.Name);
        Assert.Equal(dt, updated.DateTime);
        Assert.Equal(400, updated.EventCapacity);
    }

    [Fact]
    public async Task UpdateAsync_IssuesTheExtraTickets_WhenCapacityGrows()
    {
        var existing = ExistingEventAtVenue(eventCapacity: 100);
        TicketsSoldFor("event-1", 30);
        TicketsIssuedFor("event-1", 100);
        IReadOnlyCollection<Ticket>? toIssue = null;
        var toRemove = -1;
        CaptureUpdate(existing, (issue, remove) => (toIssue, toRemove) = (issue, remove));

        await _delegate.UpdateAsync("event-1", "Old", DateTimeOffset.UtcNow.AddDays(1), 150, "venue-1");

        Assert.NotNull(toIssue);
        Assert.Equal(50, toIssue.Count);
        Assert.All(toIssue, ticket =>
        {
            Assert.Equal("event-1", ticket.EventId);
            Assert.Equal(TicketStatus.Issued, ticket.Status);
        });
        Assert.Equal(0, toRemove);
    }

    [Fact]
    public async Task UpdateAsync_RemovesUnsoldTickets_WhenCapacityShrinks()
    {
        var existing = ExistingEventAtVenue(eventCapacity: 100);
        TicketsSoldFor("event-1", 30);
        TicketsIssuedFor("event-1", 100);
        IReadOnlyCollection<Ticket>? toIssue = null;
        var toRemove = -1;
        CaptureUpdate(existing, (issue, remove) => (toIssue, toRemove) = (issue, remove));

        await _delegate.UpdateAsync("event-1", "Old", DateTimeOffset.UtcNow.AddDays(1), 40, "venue-1");

        Assert.NotNull(toIssue);
        Assert.Empty(toIssue);
        Assert.Equal(60, toRemove);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenCapacityIsBelowSoldTickets()
    {
        var existing = ExistingEventAtVenue(eventCapacity: 100);
        TicketsSoldFor("event-1", 10);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.UpdateAsync("event-1", "New Name", DateTimeOffset.UtcNow.AddDays(1), 9, "venue-1"));

        // A refused update must leave the tracked entity untouched.
        Assert.Equal(100, existing.EventCapacity);
        _eventRepoMock.Verify(
            repo => repo.UpdateWithTicketsAsync(
                It.IsAny<Event>(), It.IsAny<IReadOnlyCollection<Ticket>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_AllowsCapacityEqualToSoldTickets()
    {
        var existing = ExistingEventAtVenue(eventCapacity: 100);
        TicketsSoldFor("event-1", 10);
        TicketsIssuedFor("event-1", 100);
        var toRemove = -1;
        CaptureUpdate(existing, (_, remove) => toRemove = remove);

        var updated = await _delegate.UpdateAsync(
            "event-1", "New Name", DateTimeOffset.UtcNow.AddDays(1), 10, "venue-1");

        Assert.Equal(10, updated.EventCapacity);
        Assert.Equal(90, toRemove);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenTicketsAreSoldBeforeTheResizeLands()
    {
        var existing = ExistingEventAtVenue(eventCapacity: 100);
        TicketsSoldFor("event-1", 10);
        TicketsIssuedFor("event-1", 100);
        CaptureUpdate(existing, (_, _) => { }, succeeds: false);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.UpdateAsync("event-1", "New Name", DateTimeOffset.UtcNow.AddDays(1), 10, "venue-1"));
    }

    [Fact]
    public async Task DeleteAsync_Throws_WhenNotFound()
    {
        TicketsSoldFor("missing", 0);
        _eventRepoMock
            .Setup(repo => repo.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _delegate.DeleteAsync("missing"));
    }

    [Fact]
    public async Task DeleteAsync_Succeeds_WhenNoTicketsHaveBeenSold()
    {
        TicketsSoldFor("event-1", 0);
        _eventRepoMock
            .Setup(repo => repo.DeleteAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _delegate.DeleteAsync("event-1");
        _eventRepoMock.Verify(repo => repo.DeleteAsync("event-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Throws_WhenTicketsHaveBeenSold()
    {
        TicketsSoldFor("event-1", 1);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.DeleteAsync("event-1"));

        // Never reaches the database, where the foreign key would have produced a 500.
        _eventRepoMock.Verify(
            repo => repo.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
