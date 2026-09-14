using System.Net;
using System.Net.Http.Json;
using M3A.Api.Dtos;

namespace M3A.Api.Tests.Routes;

/// <summary>Route suite covering the full contract for <c>/venues</c>.</summary>
public class VenueRoutesTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    [Fact]
    public async Task GetAll_Returns200()
    {
        var response = await Client.GetAsync("/venues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await ReadAsync<List<VenueDto>>(response));
    }

    [Fact]
    public async Task GetById_Returns400_WhenIdFormatIsInvalid()
    {
        var response = await Client.GetAsync("/venues/not%20an%20id");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404_WhenNotFound()
    {
        var response = await Client.GetAsync("/venues/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns201WithLocation_AndTheVenueIsRetrievable()
    {
        var response = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Foro Sol", 65000), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await ReadAsync<VenueDto>(response);
        Assert.NotNull(created);
        Assert.Equal(65000, created.Capacity);

        var fetched = await Client.GetAsync($"/venues/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400_WhenLocationIsEmpty()
    {
        var response = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto(string.Empty, 100), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400_WhenCapacityIsNotPositive()
    {
        var response = await Client.PostAsJsonAsync(
            "/venues", new CreateVenueDto("Foro Sol", 0), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns200_WhenVenueExists()
    {
        var created = await ReadAsync<VenueDto>(
            await Client.PostAsJsonAsync("/venues", new CreateVenueDto("Before", 100), JsonOptions));
        Assert.NotNull(created);

        var response = await Client.PutAsJsonAsync(
            $"/venues/{created.Id}", new UpdateVenueDto("After", 250), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<VenueDto>(response);
        Assert.Equal("After", updated?.Location);
        Assert.Equal(250, updated?.Capacity);
    }

    [Fact]
    public async Task Put_Returns404_WhenVenueDoesNotExist()
    {
        var response = await Client.PutAsJsonAsync(
            "/venues/does-not-exist", new UpdateVenueDto("After", 250), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_ThenSubsequentDeleteReturns404()
    {
        var created = await ReadAsync<VenueDto>(
            await Client.PostAsJsonAsync("/venues", new CreateVenueDto("Doomed", 10), JsonOptions));
        Assert.NotNull(created);

        var first = await Client.DeleteAsync($"/venues/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await Client.DeleteAsync($"/venues/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }
}
