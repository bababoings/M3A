using M3A.Domain.Entities;
using M3A.Domain.Exceptions;
using M3A.Domain.ValueObjects;
using M3A.Repositories;

namespace M3A.Delegates;

/// <summary>
/// Default <see cref="IItemDelegate"/>. Business rules live here, not in the routes
/// and not in the repository.
/// </summary>
public sealed class ItemDelegate(IItemRepository itemRepository) : IItemDelegate
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken cancellationToken = default) =>
        itemRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Item?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        itemRepository.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<Item> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var item = new Item { Id = ResourceId.New(), Name = name };
        await itemRepository.AddAsync(item, cancellationToken);
        return item;
    }

    /// <inheritdoc />
    public async Task<Item> UpdateAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        var item = await itemRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Item), id);

        item.Name = name;
        await itemRepository.UpdateAsync(item, cancellationToken);
        return item;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!await itemRepository.DeleteAsync(id, cancellationToken))
        {
            throw new EntityNotFoundException(nameof(Item), id);
        }
    }
}
