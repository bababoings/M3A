using M3A.Domain.Entities;
using M3A.Domain.Exceptions;
using M3A.Domain.ValueObjects;
using M3A.Repositories;

namespace M3A.Delegates;

/// <summary>
/// Default <see cref="IVenueDelegate"/>. Business rules live here, not in the routes
/// and not in the repository.
/// </summary>
public sealed class VenueDelegate(IVenueRepository venueRepository) : IVenueDelegate
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Venue>> GetAllAsync(CancellationToken cancellationToken = default) =>
        venueRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Venue?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        venueRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<Venue> CreateAsync(
        string location,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacityIsPositive(capacity);

        var venue = new Venue { Id = ResourceId.New(), Location = location, Capacity = capacity };
        await venueRepository.AddAsync(venue, cancellationToken);
        return venue;
    }

    /// <inheritdoc />
    public async Task<Venue> UpdateAsync(
        string id,
        string location,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacityIsPositive(capacity);

        var venue = await venueRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Venue), id);

        venue.Location = location;
        venue.Capacity = capacity;
        await venueRepository.UpdateAsync(venue, cancellationToken);
        return venue;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!await venueRepository.DeleteAsync(id, cancellationToken))
        {
            throw new EntityNotFoundException(nameof(Venue), id);
        }
    }

    /// <summary>
    /// A venue that holds nobody is not a venue. Enforced here as well as in the request
    /// validators, so the rule holds for every caller of the delegate layer.
    /// </summary>
    private static void EnsureCapacityIsPositive(int capacity)
    {
        if (capacity <= 0)
        {
            throw new BusinessRuleViolationException(
                $"Venue capacity must be greater than zero, but was {capacity}.");
        }
    }
}
