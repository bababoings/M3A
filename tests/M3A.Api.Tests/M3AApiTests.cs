using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M3A.Api.Tests;

/// <summary>
/// Base class for every route test: one isolated API instance and client per test class.
/// </summary>
public abstract class M3AApiTests : IClassFixture<M3AApiFactory>
{
    /// <summary>
    /// JSON options matching the API's contract: camelCase, and enums as strings just as
    /// <c>AddApiServices</c> configures them.
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Creates a client bound to the shared in-memory API instance.</summary>
    protected M3AApiTests(M3AApiFactory factory) => Client = factory.CreateClient();

    /// <summary>HTTP client for the API under test.</summary>
    protected HttpClient Client { get; }

    /// <summary>Reads and deserializes a response body using the API's JSON contract.</summary>
    protected static async Task<T?> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions);
}
