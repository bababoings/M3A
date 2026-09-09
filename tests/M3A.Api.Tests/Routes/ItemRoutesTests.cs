using System.Net;
using System.Net.Http.Json;
using M3A.Api.Dtos;

namespace M3A.Api.Tests.Routes;

/// <summary>
/// Reference route suite covering the full contract for <c>/items</c>.
/// Duplicate this shape for every resource.
/// </summary>
public class ItemRoutesTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    [Fact]
    public async Task GetAll_Returns200()
    {
        var response = await Client.GetAsync("/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await ReadAsync<List<ItemDto>>(response));
    }

    [Fact]
    public async Task GetById_Returns400_WhenIdFormatIsInvalid()
    {
        var response = await Client.GetAsync("/items/not%20an%20id");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404_WhenNotFound()
    {
        var response = await Client.GetAsync("/items/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns201WithLocation_AndTheItemIsRetrievable()
    {
        var response = await Client.PostAsJsonAsync("/items", new CreateItemDto("Ticket"), JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await ReadAsync<ItemDto>(response);
        Assert.NotNull(created);

        var fetched = await Client.GetAsync($"/items/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Post_Returns400_WhenValidationFails()
    {
        var response = await Client.PostAsJsonAsync("/items", new CreateItemDto(string.Empty), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns200_WhenItemExists()
    {
        var created = await ReadAsync<ItemDto>(
            await Client.PostAsJsonAsync("/items", new CreateItemDto("Before"), JsonOptions));
        Assert.NotNull(created);

        var response = await Client.PutAsJsonAsync(
            $"/items/{created.Id}", new UpdateItemDto("After"), JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("After", (await ReadAsync<ItemDto>(response))?.Name);
    }

    [Fact]
    public async Task Put_Returns404_WhenItemDoesNotExist()
    {
        var response = await Client.PutAsJsonAsync(
            "/items/does-not-exist", new UpdateItemDto("After"), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_ThenSubsequentDeleteReturns404()
    {
        var created = await ReadAsync<ItemDto>(
            await Client.PostAsJsonAsync("/items", new CreateItemDto("Doomed"), JsonOptions));
        Assert.NotNull(created);

        var first = await Client.DeleteAsync($"/items/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await Client.DeleteAsync($"/items/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }
}
