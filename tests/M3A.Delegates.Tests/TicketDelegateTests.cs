using M3A.Domain.Entities;
using M3A.Domain.Enums;
using M3A.Domain.Exceptions;
using M3A.Repositories;
using M3A.Tests.Builders;
using Moq;

namespace M3A.Delegates.Tests;

/// <summary>
/// Delegate suite for <c>Ticket</c>: repositories are mocked, so orchestration
/// and business rules are under test.
/// </summary>
public class TicketDelegateTests
{
    private readonly Mock<ITicketRepository> _ticketRepoMock = new(MockBehavior.Strict);
    private readonly Mock<IEventRepository> _eventRepoMock = new(MockBehavior.Strict);
    private readonly ITicketDelegate _delegate;

    public TicketDelegateTests() =>
        _delegate = new TicketDelegate(_ticketRepoMock.Object, _eventRepoMock.Object);

    [Fact]
    public async Task CreateAsync_Throws_WhenEventDoesNotExist()
    {
        _eventRepoMock
            .Setup(repo => repo.ExistsAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.CreateAsync("missing"));

        Assert.Equal(nameof(Event), exception.EntityName);
        Assert.Equal("missing", exception.Id);

        // The ticket must never reach the repository, or the database would reject it anyway.
        _ticketRepoMock.Verify(
            repo => repo.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_AssignsIdAndPersists_WhenEventExists()
    {
        _eventRepoMock
            .Setup(repo => repo.ExistsAsync("event-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Ticket? persisted = null;
        _ticketRepoMock
            .Setup(repo => repo.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, CancellationToken>((ticket, _) => persisted = ticket)
            .Returns(Task.CompletedTask);

        var result = await _delegate.CreateAsync("event-1");

        Assert.NotNull(persisted);
        Assert.Same(persisted, result);
        Assert.Equal("event-1", result.EventId);
        Assert.True(Domain.ValueObjects.ResourceId.IsValid(result.Id));
        Assert.Equal(Domain.Enums.TicketStatus.Issued, result.Status);
    }

    // ---- Status transitions -------------------------------------------------
    // Issued -> Purchased -> Redeemed. Every other move is refused, and the ticket
    // is left untouched when it is.

    [Fact]
    public async Task PurchaseAsync_MovesIssuedToPurchased()
    {
        var ticket = new TicketBuilder().WithStatus(TicketStatus.Issued).Build();
        ExpectLookup(ticket);
        ExpectUpdate();

        var result = await _delegate.PurchaseAsync(ticket.Id);

        Assert.Equal(TicketStatus.Purchased, result.Status);
        _ticketRepoMock.Verify(
            repo => repo.UpdateAsync(
                It.Is<Ticket>(t => t.Status == TicketStatus.Purchased),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RedeemAsync_MovesPurchasedToRedeemed()
    {
        var ticket = new TicketBuilder().WithStatus(TicketStatus.Purchased).Build();
        ExpectLookup(ticket);
        ExpectUpdate();

        var result = await _delegate.RedeemAsync(ticket.Id);

        Assert.Equal(TicketStatus.Redeemed, result.Status);
        _ticketRepoMock.Verify(
            repo => repo.UpdateAsync(
                It.Is<Ticket>(t => t.Status == TicketStatus.Redeemed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(TicketStatus.Purchased)]  // already bought
    [InlineData(TicketStatus.Redeemed)]   // already used
    public async Task PurchaseAsync_Throws_WhenTicketIsNotIssued(TicketStatus status)
    {
        var ticket = new TicketBuilder().WithStatus(status).Build();
        ExpectLookup(ticket);

        var exception = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.PurchaseAsync(ticket.Id));

        Assert.Contains(status.ToString(), exception.Message, StringComparison.Ordinal);
        AssertNotPersisted(ticket, status);
    }

    [Theory]
    [InlineData(TicketStatus.Issued)]     // not bought yet
    [InlineData(TicketStatus.Redeemed)]   // already used
    public async Task RedeemAsync_Throws_WhenTicketIsNotPurchased(TicketStatus status)
    {
        var ticket = new TicketBuilder().WithStatus(status).Build();
        ExpectLookup(ticket);

        var exception = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.RedeemAsync(ticket.Id));

        Assert.Contains(status.ToString(), exception.Message, StringComparison.Ordinal);
        AssertNotPersisted(ticket, status);
    }

    [Fact]
    public async Task PurchaseAsync_Throws_WhenTicketNotFound()
    {
        ExpectMissing("missing");

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.PurchaseAsync("missing"));

        Assert.Equal(nameof(Ticket), exception.EntityName);
        Assert.Equal("missing", exception.Id);
    }

    [Fact]
    public async Task RedeemAsync_Throws_WhenTicketNotFound()
    {
        ExpectMissing("missing");

        var exception = await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.RedeemAsync("missing"));

        Assert.Equal(nameof(Ticket), exception.EntityName);
        Assert.Equal("missing", exception.Id);
    }

    private void ExpectLookup(Ticket ticket) =>
        _ticketRepoMock
            .Setup(repo => repo.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

    private void ExpectMissing(string id) =>
        _ticketRepoMock
            .Setup(repo => repo.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Ticket?)null);

    private void ExpectUpdate() =>
        _ticketRepoMock
            .Setup(repo => repo.UpdateAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    /// <summary>A refused transition must neither persist nor mutate the entity in memory.</summary>
    private void AssertNotPersisted(Ticket ticket, TicketStatus original)
    {
        Assert.Equal(original, ticket.Status);
        _ticketRepoMock.Verify(
            repo => repo.UpdateAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
