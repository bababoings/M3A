using M3A.Tests.Builders;

namespace M3A.Repositories.Tests;

/// <summary>Repository suite for the <c>Venue</c> aggregate root.</summary>
public class VenueRepositoryTests : RepositoryTestBase
{
    private readonly IVenueRepository _repository;

    /// <summary>Builds the repository over a per-test in-memory database.</summary>
    public VenueRepositoryTests() => _repository = new VenueRepository(DbContext);

    [Fact]
    public async Task GetAllAsync_ReturnsEmpty_WhenNoVenues() =>
        Assert.Empty(await _repository.GetAllAsync(CancellationToken.None));

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsTheEntity()
    {
        var cancellationToken = CancellationToken.None;
        var venue = new VenueBuilder()
            .WithId("venue-1")
            .WithLocation("Foro Sol")
            .WithCapacity(65000)
            .Build();

        await _repository.AddAsync(venue, cancellationToken);
        var found = await _repository.GetByIdAsync("venue-1", cancellationToken);

        Assert.NotNull(found);
        Assert.Equal("Foro Sol", found.Location);
        Assert.Equal(65000, found.Capacity);
    }

    [Fact]
    public async Task ExistsAsync_ReflectsWhetherTheVenueWasAdded()
    {
        var cancellationToken = CancellationToken.None;
        Assert.False(await _repository.ExistsAsync("venue-1", cancellationToken));

        await _repository.AddAsync(new VenueBuilder().WithId("venue-1").Build(), cancellationToken);

        Assert.True(await _repository.ExistsAsync("venue-1", cancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenIdIsUnknown() =>
        Assert.False(await _repository.DeleteAsync("missing", CancellationToken.None));
}
