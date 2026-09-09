namespace M3A.Api.Dtos;

/// <summary>Representation of an item returned by the API.</summary>
/// <param name="Id">Resource identifier.</param>
/// <param name="Name">Human readable name.</param>
public sealed record ItemDto(string Id, string Name);

/// <summary>Payload for <c>POST /items</c>.</summary>
/// <param name="Name">Human readable name.</param>
public sealed record CreateItemDto(string Name);

/// <summary>Payload for <c>PUT /items/{itemId}</c>.</summary>
/// <param name="Name">Human readable name.</param>
public sealed record UpdateItemDto(string Name);
