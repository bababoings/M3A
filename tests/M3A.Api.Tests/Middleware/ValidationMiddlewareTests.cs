using System.Net;
using System.Net.Http.Json;
using System.Text;
using M3A.Api.Dtos;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace M3A.Api.Tests.Middleware;

/// <summary>
/// Cross-cutting tests for the error contract: ProblemDetails shape, content type,
/// and id format rejection. These apply to every resource.
/// </summary>
public class ValidationMiddlewareTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    private readonly M3AApiFactory _factory = factory;

    [Fact]
    public async Task ValidationFailure_ReturnsProblemDetailsWithErrors()
    {
        var response = await Client.PostAsJsonAsync("/venues", new CreateVenueDto(string.Empty, 0), JsonOptions);

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

        var response = await Client.PostAsync("/venues", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_Returns400_WhenBadRequestsThrow()
    {
        // Development turns on ThrowOnBadRequest, which routes the failure through
        // GlobalExceptionHandler instead of the framework's own 400.
        using var client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true)))
            .CreateClient();
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/venues", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadAsync<ProblemDetails>(response);
        Assert.Equal("Bad Request", problem?.Title);
    }

    [Fact]
    public async Task SuccessfulResponse_IsJson()
    {
        var response = await Client.GetAsync("/venues");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/venues/has%20space")]
    [InlineData("/venues/under_score")]
    public async Task InvalidIdFormat_Returns400(string path)
    {
        var response = await Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
