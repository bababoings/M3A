using System.Net;
using System.Net.Http.Json;
using M3A.Api.Dtos;

namespace M3A.Api.Tests.Routes;

/// <summary>Route suite covering the full contract for <c>/events</c>.</summary>
public class EventRoutesTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    [Fact]
    public async Task GetAll_Returns200()
    {
        var response = await Client.GetAsync("/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await ReadAsync<List<EventDto>>(response));
    }

    [Fact]
    public async Task GetById_Returns400_WhenIdFormatIsInvalid()
    {
        var response = await Client.GetAsync("/events/not%20an%20id");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404_WhenNotFound()
    {
        var response = await Client.GetAsync("/events/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns201WithLocation_AndTheEventIsRetrievableWithVenue()
    {
        // 1. Create a hosting venue first
        var venueResponse = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Estadio Azteca", 87000), JsonOptions);
        var venue = await ReadAsync<VenueDto>(venueResponse);
        Assert.NotNull(venue);

        // 2. Create the event
        var eventDate = DateTimeOffset.UtcNow.AddDays(30);
        var createDto = new CreateEventDto("World Cup Match", eventDate, 80000, venue.Id);
        var response = await Client.PostAsJsonAsync("/events", createDto, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await ReadAsync<EventDto>(response);
        Assert.NotNull(created);
        Assert.Equal("World Cup Match", created.Name);
        Assert.Equal(80000, created.EventCapacity);
        Assert.Equal(venue.Id, created.VenueId);
        Assert.NotNull(created.Venue);
        Assert.Equal("Estadio Azteca", created.Venue.Location);

        // 3. Fetch by ID
        var fetched = await Client.GetAsync($"/events/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400_WhenNameIsEmpty()
    {
        var createDto = new CreateEventDto(
            string.Empty, DateTimeOffset.UtcNow.AddDays(1), 100, "venue-1");
        var response = await Client.PostAsJsonAsync("/events", createDto, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400_WhenCapacityIsNotPositive()
    {
        var createDto = new CreateEventDto(
            "Concert", DateTimeOffset.UtcNow.AddDays(1), 0, "venue-1");
        var response = await Client.PostAsJsonAsync("/events", createDto, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400_WhenDateTimeIsInThePast()
    {
        var createDto = new CreateEventDto(
            "Concert", DateTimeOffset.UtcNow.AddDays(-1), 100, "venue-1");
        var response = await Client.PostAsJsonAsync("/events", createDto, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns404_WhenVenueDoesNotExist()
    {
        var createDto = new CreateEventDto(
            "Concert", DateTimeOffset.UtcNow.AddDays(10), 100, "nonexistent-venue");
        var response = await Client.PostAsJsonAsync("/events", createDto, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns422_WhenEventCapacityExceedsVenueCapacity()
    {
        var venueResponse = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Intimate Club", 200), JsonOptions);
        var venue = await ReadAsync<VenueDto>(venueResponse);
        Assert.NotNull(venue);

        var createDto = new CreateEventDto(
            "Huge Festival", DateTimeOffset.UtcNow.AddDays(5), 500, venue.Id);
        var response = await Client.PostAsJsonAsync("/events", createDto, JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns200_WhenEventExists()
    {
        var venueResponse = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Arena", 5000), JsonOptions);
        var venue = await ReadAsync<VenueDto>(venueResponse);
        Assert.NotNull(venue);

        var createResponse = await Client.PostAsJsonAsync(
            "/events", new CreateEventDto("Original", DateTimeOffset.UtcNow.AddDays(1), 1000, venue.Id), JsonOptions);
        var created = await ReadAsync<EventDto>(createResponse);
        Assert.NotNull(created);

        var updateDate = DateTimeOffset.UtcNow.AddDays(2);
        var updateDto = new UpdateEventDto("Updated Name", updateDate, 2000, venue.Id);
        var putResponse = await Client.PutAsJsonAsync($"/events/{created.Id}", updateDto, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var updated = await ReadAsync<EventDto>(putResponse);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal(2000, updated.EventCapacity);
    }

    [Fact]
    public async Task Put_Returns404_WhenEventDoesNotExist()
    {
        var updateDto = new UpdateEventDto(
            "Nonexistent", DateTimeOffset.UtcNow.AddDays(1), 500, "venue-1");
        var response = await Client.PutAsJsonAsync("/events/does-not-exist", updateDto, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_ThenSubsequentDeleteReturns404()
    {
        var venueResponse = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Club", 300), JsonOptions);
        var venue = await ReadAsync<VenueDto>(venueResponse);
        Assert.NotNull(venue);

        var createResponse = await Client.PostAsJsonAsync(
            "/events", new CreateEventDto("Doomed", DateTimeOffset.UtcNow.AddDays(1), 100, venue.Id), JsonOptions);
        var created = await ReadAsync<EventDto>(createResponse);
        Assert.NotNull(created);

        var first = await Client.DeleteAsync($"/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await Client.DeleteAsync($"/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }
}
