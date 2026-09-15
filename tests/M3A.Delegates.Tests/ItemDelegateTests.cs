using M3A.Domain.Entities;
using M3A.Domain.Exceptions;
using M3A.Repositories;
using M3A.Tests.Builders;
using Moq;

namespace M3A.Delegates.Tests;

/// <summary>
/// Reference delegate test suite: the repository is mocked, so only orchestration
/// and business rules are under test. Duplicate this shape for every delegate.
/// </summary>
public class ItemDelegateTests
{
    private readonly Mock<IItemRepository> _repositoryMock = new(MockBehavior.Strict);
    private readonly ItemDelegate _delegate;

    /// <summary>Wires the delegate over its mocked repository.</summary>
    public ItemDelegateTests() => _delegate = new ItemDelegate(_repositoryMock.Object);

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        _repositoryMock
            .Setup(repo => repo.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        Assert.Null(await _delegate.GetByIdAsync("missing"));
    }

    [Fact]
    public async Task CreateAsync_AssignsIdAndPersists()
    {
        _repositoryMock
            .Setup(repo => repo.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var created = await _delegate.CreateAsync("Ticket");

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Ticket", created.Name);
        _repositoryMock.Verify(
            repo => repo.AddAsync(It.Is<Item>(item => item.Name == "Ticket"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenNotFound()
    {
        _repositoryMock
            .Setup(repo => repo.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Item?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _delegate.UpdateAsync("missing", "Ticket"));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesFields_WhenFound()
    {
        var existing = new ItemBuilder().WithId("item-1").WithName("Old").Build();
        _repositoryMock
            .Setup(repo => repo.GetByIdAsync("item-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(repo => repo.UpdateAsync(existing, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var updated = await _delegate.UpdateAsync("item-1", "New");

        Assert.Equal("New", updated.Name);
    }

    [Fact]
    public async Task DeleteAsync_Throws_WhenNotFound()
    {
        _repositoryMock
            .Setup(repo => repo.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _delegate.DeleteAsync("missing"));
    }
}
