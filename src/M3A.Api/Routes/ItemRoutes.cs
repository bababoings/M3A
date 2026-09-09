using M3A.Api.Dtos;
using M3A.Api.Extensions;
using M3A.Delegates;
using M3A.Domain.Entities;
using M3A.Domain.ValueObjects;

namespace M3A.Api.Routes;

/// <summary>
/// Route module for <c>/items</c>. Every resource gets one of these; routes stay thin
/// and hand all work to the delegate layer.
/// </summary>
public static class ItemRoutes
{
    private const string BasePath = "/items";

    /// <summary>Maps every <c>/items</c> endpoint.</summary>
    public static IEndpointRouteBuilder MapItemRoutes(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(BasePath)
            .WithTags("Items");

        group.MapGet("/", GetAllAsync)
            .WithName("GetItems")
            .WithSummary("List all items")
            .Produces<IReadOnlyList<ItemDto>>();

        group.MapGet("/{itemId}", GetByIdAsync)
            .WithName("GetItemById")
            .WithSummary("Get an item by id")
            .Produces<ItemDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateItem")
            .WithSummary("Create a new item")
            .WithValidation<CreateItemDto>()
            .Produces<ItemDto>(StatusCodes.Status201Created);

        group.MapPut("/{itemId}", UpdateAsync)
            .WithName("UpdateItem")
            .WithSummary("Replace an existing item")
            .WithValidation<UpdateItemDto>()
            .Produces<ItemDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{itemId}", DeleteAsync)
            .WithName("DeleteItem")
            .WithSummary("Delete an item")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        IItemDelegate itemDelegate,
        CancellationToken cancellationToken)
    {
        var items = await itemDelegate.GetAllAsync(cancellationToken);
        return TypedResults.Ok(items.ToDtos());
    }

    private static async Task<IResult> GetByIdAsync(
        string itemId,
        IItemDelegate itemDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(itemId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(itemId), itemId);
        }

        var item = await itemDelegate.GetByIdAsync(itemId, cancellationToken);
        return item is null
            ? ResultExtensions.NotFoundProblem(nameof(Item), itemId)
            : TypedResults.Ok(item.ToDto());
    }

    private static async Task<IResult> CreateAsync(
        CreateItemDto request,
        IItemDelegate itemDelegate,
        CancellationToken cancellationToken)
    {
        var item = await itemDelegate.CreateAsync(request.Name, cancellationToken);
        return TypedResults.Created($"{BasePath}/{item.Id}", item.ToDto());
    }

    private static async Task<IResult> UpdateAsync(
        string itemId,
        UpdateItemDto request,
        IItemDelegate itemDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(itemId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(itemId), itemId);
        }

        var item = await itemDelegate.UpdateAsync(itemId, request.Name, cancellationToken);
        return TypedResults.Ok(item.ToDto());
    }

    private static async Task<IResult> DeleteAsync(
        string itemId,
        IItemDelegate itemDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(itemId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(itemId), itemId);
        }

        await itemDelegate.DeleteAsync(itemId, cancellationToken);
        return TypedResults.NoContent();
    }
}
