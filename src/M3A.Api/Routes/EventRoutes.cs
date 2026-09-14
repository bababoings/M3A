using M3A.Api.Dtos;
using M3A.Api.Extensions;
using M3A.Delegates;
using M3A.Domain.Entities;
using M3A.Domain.ValueObjects;

namespace M3A.Api.Routes;

/// <summary>
/// Route module for <c>/events</c>. Routes stay thin and hand all work to the delegate layer.
/// </summary>
public static class EventRoutes
{
    private const string BasePath = "/events";

    /// <summary>Maps every <c>/events</c> endpoint.</summary>
    public static IEndpointRouteBuilder MapEventRoutes(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(BasePath)
            .WithTags("Events");

        group.MapGet("/", GetAllAsync)
            .WithName("GetEvents")
            .WithSummary("List all events")
            .Produces<IReadOnlyList<EventDto>>();

        group.MapGet("/{eventId}", GetByIdAsync)
            .WithName("GetEventById")
            .WithSummary("Get an event by id")
            .Produces<EventDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateEvent")
            .WithSummary("Create a new event")
            .WithValidation<CreateEventDto>()
            .Produces<EventDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/{eventId}", UpdateAsync)
            .WithName("UpdateEvent")
            .WithSummary("Replace an existing event")
            .WithValidation<UpdateEventDto>()
            .Produces<EventDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{eventId}", DeleteAsync)
            .WithName("DeleteEvent")
            .WithSummary("Delete an event")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        IEventDelegate eventDelegate,
        CancellationToken cancellationToken)
    {
        var events = await eventDelegate.GetAllAsync(cancellationToken);
        return TypedResults.Ok(events.ToDtos());
    }

    private static async Task<IResult> GetByIdAsync(
        string eventId,
        IEventDelegate eventDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(eventId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(eventId), eventId);
        }

        var @event = await eventDelegate.GetByIdAsync(eventId, cancellationToken);
        return @event is null
            ? ResultExtensions.NotFoundProblem(nameof(Event), eventId)
            : TypedResults.Ok(@event.ToDto());
    }

    private static async Task<IResult> CreateAsync(
        CreateEventDto request,
        IEventDelegate eventDelegate,
        CancellationToken cancellationToken)
    {
        var @event = await eventDelegate.CreateAsync(
            request.Name,
            request.DateTime,
            request.EventCapacity,
            request.VenueId,
            cancellationToken);

        return TypedResults.Created($"{BasePath}/{@event.Id}", @event.ToDto());
    }

    private static async Task<IResult> UpdateAsync(
        string eventId,
        UpdateEventDto request,
        IEventDelegate eventDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(eventId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(eventId), eventId);
        }

        var @event = await eventDelegate.UpdateAsync(
            eventId,
            request.Name,
            request.DateTime,
            request.EventCapacity,
            request.VenueId,
            cancellationToken);

        return TypedResults.Ok(@event.ToDto());
    }

    private static async Task<IResult> DeleteAsync(
        string eventId,
        IEventDelegate eventDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(eventId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(eventId), eventId);
        }

        await eventDelegate.DeleteAsync(eventId, cancellationToken);
        return TypedResults.NoContent();
    }
}
