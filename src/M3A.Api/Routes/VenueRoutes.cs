using M3A.Api.Dtos;
using M3A.Api.Extensions;
using M3A.Delegates;
using M3A.Domain.Entities;
using M3A.Domain.ValueObjects;

namespace M3A.Api.Routes;

/// <summary>
/// Route module for <c>/venues</c>. Routes stay thin and hand all work to the delegate layer.
/// </summary>
public static class VenueRoutes
{
    private const string BasePath = "/venues";

    /// <summary>Maps every <c>/venues</c> endpoint.</summary>
    public static IEndpointRouteBuilder MapVenueRoutes(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(BasePath)
            .WithTags("Venues");

        group.MapGet("/", GetAllAsync)
            .WithName("GetVenues")
            .WithSummary("List all venues")
            .Produces<IReadOnlyList<VenueDto>>();

        group.MapGet("/{venueId}", GetByIdAsync)
            .WithName("GetVenueById")
            .WithSummary("Get a venue by id")
            .Produces<VenueDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateVenue")
            .WithSummary("Create a new venue")
            .WithValidation<CreateVenueDto>()
            .Produces<VenueDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{venueId}", UpdateAsync)
            .WithName("UpdateVenue")
            .WithSummary("Replace an existing venue")
            .WithValidation<UpdateVenueDto>()
            .Produces<VenueDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{venueId}", DeleteAsync)
            .WithName("DeleteVenue")
            .WithSummary("Delete a venue")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        IVenueDelegate venueDelegate,
        CancellationToken cancellationToken)
    {
        var venues = await venueDelegate.GetAllAsync(cancellationToken);
        return TypedResults.Ok(venues.ToDtos());
    }

    private static async Task<IResult> GetByIdAsync(
        string venueId,
        IVenueDelegate venueDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(venueId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(venueId), venueId);
        }

        var venue = await venueDelegate.GetByIdAsync(venueId, cancellationToken);
        return venue is null
            ? ResultExtensions.NotFoundProblem(nameof(Venue), venueId)
            : TypedResults.Ok(venue.ToDto());
    }

    private static async Task<IResult> CreateAsync(
        CreateVenueDto request,
        IVenueDelegate venueDelegate,
        CancellationToken cancellationToken)
    {
        var venue = await venueDelegate.CreateAsync(request.Location, request.Capacity, cancellationToken);
        return TypedResults.Created($"{BasePath}/{venue.Id}", venue.ToDto());
    }

    private static async Task<IResult> UpdateAsync(
        string venueId,
        UpdateVenueDto request,
        IVenueDelegate venueDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(venueId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(venueId), venueId);
        }

        var venue = await venueDelegate.UpdateAsync(
            venueId, request.Location, request.Capacity, cancellationToken);
        return TypedResults.Ok(venue.ToDto());
    }

    private static async Task<IResult> DeleteAsync(
        string venueId,
        IVenueDelegate venueDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(venueId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(venueId), venueId);
        }

        await venueDelegate.DeleteAsync(venueId, cancellationToken);
        return TypedResults.NoContent();
    }
}
