using System.Net;
using System.Net.Http.Json;
using System.Text;
using M3A.Api.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace M3A.Api.Tests.Middleware;

/// <summary>
/// Cross-cutting tests for the error contract: ProblemDetails shape, content type,
/// and id format rejection. These apply to every resource.
/// </summary>
public class ValidationMiddlewareTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    [Fact]
    public async Task ValidationFailure_ReturnsProblemDetailsWithErrors()
    {
        var response = await Client.PostAsJsonAsync("/items", new CreateItemDto(string.Empty), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType ?? string.Empty);

        var problem = await ReadAsync<ValidationProblemDetails>(response);
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task MalformedJson_Returns400()
    {
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/items", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SuccessfulResponse_IsJson()
    {
        var response = await Client.GetAsync("/items");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/items/has%20space")]
    [InlineData("/items/under_score")]
    public async Task InvalidIdFormat_Returns400(string path)
    {
        var response = await Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
