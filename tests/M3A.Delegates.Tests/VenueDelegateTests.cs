using M3A.Domain.Entities;
using M3A.Domain.Exceptions;
using M3A.Repositories;
using M3A.Tests.Builders;
using Moq;

namespace M3A.Delegates.Tests;

/// <summary>
/// Delegate suite for <c>Venue</c>: the repository is mocked, so only orchestration
/// and business rules are under test.
/// </summary>
public class VenueDelegateTests
{
    private readonly Mock<IVenueRepository> _repositoryMock = new(MockBehavior.Strict);
    private readonly VenueDelegate _delegate;

    /// <summary>Wires the delegate over its mocked repository.</summary>
    public VenueDelegateTests() => _delegate = new VenueDelegate(_repositoryMock.Object);

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        _repositoryMock
            .Setup(repo => repo.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Venue?)null);

        Assert.Null(await _delegate.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task CreateAsync_AssignsIdAndPersists()
    {
        _repositoryMock
            .Setup(repo => repo.AddAsync(It.IsAny<Venue>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var created = await _delegate.CreateAsync("Foro Sol", 65000);

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Foro Sol", created.Location);
        Assert.Equal(65000, created.Capacity);
        _repositoryMock.Verify(
            repo => repo.AddAsync(
                It.Is<Venue>(venue => venue.Location == "Foro Sol" && venue.Capacity == 65000),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_Throws_WhenCapacityIsNotPositive(int capacity) =>
        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.CreateAsync("Foro Sol", capacity));

    [Fact]
    public async Task UpdateAsync_Throws_WhenNotFound()
    {
        _repositoryMock
            .Setup(repo => repo.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Venue?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.UpdateAsync("missing", "Foro Sol", 65000));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesFields_WhenFound()
    {
        var existing = new VenueBuilder()
            .WithId("venue-1")
            .WithLocation("Old")
            .WithCapacity(10)
            .Build();
        _repositoryMock
            .Setup(repo => repo.GetByIdAsync("venue-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(repo => repo.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var updated = await _delegate.UpdateAsync("venue-1", "New", 500);

        Assert.Equal("New", updated.Location);
        Assert.Equal(500, updated.Capacity);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenCapacityIsNotPositive() =>
        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => _delegate.UpdateAsync("venue-1", "Foro Sol", 0));

    [Fact]
    public async Task DeleteAsync_Throws_WhenNotFound()
    {
        _repositoryMock
            .Setup(repo => repo.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _delegate.DeleteAsync("missing"));
    }
}
