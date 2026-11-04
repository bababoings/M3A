using System.Net;
using M3A.Api.Extensions;
using Microsoft.AspNetCore.Http;

namespace M3A.Api.Tests.Routes;

/// <summary>Contract for the Prometheus scraping endpoint <c>/metrics</c>.</summary>
public class MetricsRoutesTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    [Fact]
    public async Task Metrics_ExposesHttpRequestHistogram_InPrometheusFormat()
    {
        // Generate at least one measured request first.
        await Client.GetAsync("/health/live");

        var response = await Client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http_server_request_duration_seconds_bucket", body);
    }

    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/metrics", true)]
    [InlineData("/events", false)]
    [InlineData("/healthy", false)]
    public void IsInfrastructurePath_ExcludesOnlyProbesAndScrapes(string path, bool expected) =>
        Assert.Equal(expected, TelemetryExtensions.IsInfrastructurePath(new PathString(path)));
}
