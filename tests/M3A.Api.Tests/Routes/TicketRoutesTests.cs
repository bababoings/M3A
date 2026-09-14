using System.Net;
using System.Net.Http.Json;
using M3A.Api.Dtos;
using M3A.Domain.Enums;

namespace M3A.Api.Tests.Routes;

/// <summary>
/// Route coverage for the <c>/tickets</c> contract: creation against a real event, and the
/// <c>Issued -> Purchased -> Redeemed</c> transitions with their status-code mapping.
/// </summary>
public class TicketRoutesTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    [Fact]
    public async Task Post_Returns404_WhenEventDoesNotExist()
    {
        var response = await Client.PostAsJsonAsync(
            "/tickets", new CreateTicketDto("does-not-exist"), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns201WithLocation_WhenEventExists()
    {
        var eventId = await CreateEventAsync();

        var response = await Client.PostAsJsonAsync(
            "/tickets", new CreateTicketDto(eventId), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await ReadAsync<TicketDto>(response);
        Assert.NotNull(created);
        Assert.Equal(eventId, created.EventId);
        Assert.Equal(TicketStatus.Issued, created.Status);
    }

    [Fact]
    public async Task Purchase_Returns200_AndMovesTicketToPurchased()
    {
        var ticket = await IssueTicketAsync();

        var response = await Client.PostAsync($"/tickets/{ticket}/purchase", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync<TicketDto>(response);
        Assert.NotNull(updated);
        Assert.Equal(TicketStatus.Purchased, updated.Status);
    }

    [Fact]
    public async Task Redeem_Returns200_AndMovesTicketToRedeemed()
    {
        var ticket = await IssueTicketAsync();
        await Client.PostAsync($"/tickets/{ticket}/purchase", null);

        var response = await Client.PostAsync($"/tickets/{ticket}/redeem", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync<TicketDto>(response);
        Assert.NotNull(updated);
        Assert.Equal(TicketStatus.Redeemed, updated.Status);

        // And the new status is what a later read returns.
        var fetched = await ReadAsync<TicketDto>(await Client.GetAsync($"/tickets/{ticket}"));
        Assert.NotNull(fetched);
        Assert.Equal(TicketStatus.Redeemed, fetched.Status);
    }

    [Fact]
    public async Task Purchase_Returns422_WhenTicketIsAlreadyPurchased()
    {
        var ticket = await IssueTicketAsync();
        await Client.PostAsync($"/tickets/{ticket}/purchase", null);

        var response = await Client.PostAsync($"/tickets/{ticket}/purchase", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Redeem_Returns422_WhenTicketHasNotBeenPurchased()
    {
        var ticket = await IssueTicketAsync();

        var response = await Client.PostAsync($"/tickets/{ticket}/redeem", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // The refused transition left the ticket alone.
        var fetched = await ReadAsync<TicketDto>(await Client.GetAsync($"/tickets/{ticket}"));
        Assert.NotNull(fetched);
        Assert.Equal(TicketStatus.Issued, fetched.Status);
    }

    [Fact]
    public async Task Redeem_Returns422_WhenTicketIsAlreadyRedeemed()
    {
        var ticket = await IssueTicketAsync();
        await Client.PostAsync($"/tickets/{ticket}/purchase", null);
        await Client.PostAsync($"/tickets/{ticket}/redeem", null);

        var response = await Client.PostAsync($"/tickets/{ticket}/redeem", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("purchase")]
    [InlineData("redeem")]
    public async Task Transition_Returns404_WhenTicketDoesNotExist(string action)
    {
        var response = await Client.PostAsync($"/tickets/does-not-exist/{action}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("purchase")]
    [InlineData("redeem")]
    public async Task Transition_Returns400_WhenIdFormatIsInvalid(string action)
    {
        var response = await Client.PostAsync($"/tickets/not%20an%20id/{action}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Creates an event and issues a ticket on it, returning the ticket id.</summary>
    private async Task<string> IssueTicketAsync()
    {
        var response = await Client.PostAsJsonAsync(
            "/tickets", new CreateTicketDto(await CreateEventAsync()), JsonOptions);
        var ticket = await ReadAsync<TicketDto>(response);
        Assert.NotNull(ticket);
        return ticket.Id;
    }

    /// <summary>Creates a venue and an event on it, returning the event id.</summary>
    private async Task<string> CreateEventAsync()
    {
        var venueResponse = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Foro Sol", 65000), JsonOptions);
        var venue = await ReadAsync<VenueDto>(venueResponse);
        Assert.NotNull(venue);

        var eventResponse = await Client.PostAsJsonAsync(
            "/events",
            new CreateEventDto("Concert", DateTimeOffset.UtcNow.AddDays(14), 50000, venue.Id),
            JsonOptions);
        var @event = await ReadAsync<EventDto>(eventResponse);
        Assert.NotNull(@event);

        return @event.Id;
    }
}
