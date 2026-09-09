using M3A.Api.Dtos;
using M3A.Domain.Entities;

namespace M3A.Api.Extensions;

/// <summary>
/// Entity to DTO mapping. Keeping it in the API layer is what lets the inner layers
/// stay unaware of the wire contract.
/// </summary>
public static class ItemMappings
{
    /// <summary>Projects a single entity onto its wire representation.</summary>
    public static ItemDto ToDto(this Item item) => new(item.Id, item.Name);

    /// <summary>Projects a collection of entities onto their wire representation.</summary>
    public static IReadOnlyList<ItemDto> ToDtos(this IEnumerable<Item> items) =>
        items.Select(ToDto).ToList();
}
