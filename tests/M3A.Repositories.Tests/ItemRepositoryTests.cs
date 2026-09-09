using M3A.Tests.Builders;

namespace M3A.Repositories.Tests;

/// <summary>
/// Reference repository test suite. Duplicate this shape for every aggregate root.
/// </summary>
public class ItemRepositoryTests : RepositoryTestBase
{
    private readonly IItemRepository _repository;

    /// <summary>Builds the repository over a per-test in-memory database.</summary>
    public ItemRepositoryTests() => _repository = new ItemRepository(DbContext);

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenNoItems() =>
        Assert.Empty(await _repository.GetAllAsync(CancellationToken.None));

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsTheEntity()
    {
        var cancellationToken = CancellationToken.None;
        var item = new ItemBuilder().WithId("item-1").WithName("Ticket").Build();

        await _repository.AddAsync(item, cancellationToken);
        var found = await _repository.GetByIdAsync("item-1", cancellationToken);

        Assert.NotNull(found);
        Assert.Equal("Ticket", found.Name);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenIdIsUnknown() =>
        Assert.False(await _repository.DeleteAsync("missing", CancellationToken.None));
}
