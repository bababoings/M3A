using M3A.Api.Dtos;
using M3A.Api.Extensions;
using M3A.Delegates;
using M3A.Domain.Entities;
using M3A.Domain.ValueObjects;

namespace M3A.Api.Routes;

/// <summary>Ticket endpoints.</summary>
public static class TicketRoutes
{
    private const string BasePath = "/tickets";

    public static IEndpointRouteBuilder MapTicketRoutes(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(BasePath)
            .WithTags("Tickets");

        group.MapGet("/", GetAllAsync)
            .WithName("GetTickets")
            .WithSummary("List all tickets")
            .Produces<IReadOnlyList<TicketDto>>();

        group.MapGet("/{ticketId}", GetByIdAsync)
            .WithName("GetTicketById")
            .WithSummary("Get a ticket by id")
            .Produces<TicketDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateTicket")
            .WithSummary("Issue a new ticket for an event")
            .WithValidation<CreateTicketDto>()
            .Produces<TicketDto>(StatusCodes.Status201Created);

        group.MapDelete("/{ticketId}", DeleteAsync)
            .WithName("DeleteTicket")
            .WithSummary("Delete a ticket")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{ticketId}/purchase", PurchaseAsync)
            .WithName("PurchaseTicket")
            .WithSummary("Purchase an issued ticket")
            .Produces<TicketDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{ticketId}/redeem", RedeemAsync)
            .WithName("RedeemTicket")
            .WithSummary("Redeem a purchased ticket")
            .Produces<TicketDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        ITicketDelegate ticketDelegate,
        CancellationToken cancellationToken)
    {
        var tickets = await ticketDelegate.GetAllAsync(cancellationToken);
        return TypedResults.Ok(tickets.ToDtos());
    }

    private static async Task<IResult> GetByIdAsync(
        string ticketId,
        ITicketDelegate ticketDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(ticketId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(ticketId), ticketId);
        }

        var ticket = await ticketDelegate.GetByIdAsync(ticketId, cancellationToken);
        return ticket is null
            ? ResultExtensions.NotFoundProblem(nameof(Ticket), ticketId)
            : TypedResults.Ok(ticket.ToDto());
    }

    private static async Task<IResult> CreateAsync(
        CreateTicketDto request,
        ITicketDelegate ticketDelegate,
        CancellationToken cancellationToken)
    {
        var ticket = await ticketDelegate.CreateAsync(request.EventId, cancellationToken);
        return TypedResults.Created($"{BasePath}/{ticket.Id}", ticket.ToDto());
    }

    private static async Task<IResult> DeleteAsync(
        string ticketId,
        ITicketDelegate ticketDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(ticketId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(ticketId), ticketId);
        }

        await ticketDelegate.DeleteAsync(ticketId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PurchaseAsync(
        string ticketId,
        ITicketDelegate ticketDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(ticketId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(ticketId), ticketId);
        }

        var ticket = await ticketDelegate.PurchaseAsync(ticketId, cancellationToken);
        return TypedResults.Ok(ticket.ToDto());
    }

    private static async Task<IResult> RedeemAsync(
        string ticketId,
        ITicketDelegate ticketDelegate,
        CancellationToken cancellationToken)
    {
        if (!ResourceId.IsValid(ticketId))
        {
            return ResultExtensions.InvalidIdProblem(nameof(ticketId), ticketId);
        }

        var ticket = await ticketDelegate.RedeemAsync(ticketId, cancellationToken);
        return TypedResults.Ok(ticket.ToDto());
    }
}
