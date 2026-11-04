using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace M3A.Api.Extensions;

/// <summary>
/// OpenTelemetry wiring:
/// <list type="bullet">
///   <item><b>Metrics</b> — ASP.NET Core HTTP, .NET runtime and Npgsql client/pool metrics,
///   exposed for Prometheus at <c>GET /metrics</c>.</item>
///   <item><b>Traces</b> — ASP.NET Core request spans with nested Npgsql command spans, exported
///   over OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (e.g. to Tempo).</item>
/// </list>
/// The service name defaults to <see cref="DefaultServiceName"/>; <c>OTEL_SERVICE_NAME</c>
/// and the other standard <c>OTEL_*</c> variables (sampler, protocol) override it.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>Service name reported when <c>OTEL_SERVICE_NAME</c> is unset.</summary>
    public const string DefaultServiceName = "m3a-api";

    /// <summary>Prometheus scraping path.</summary>
    public const string MetricsPath = "/metrics";

    /// <summary>Npgsql's built-in <c>Meter</c> and <c>ActivitySource</c> name.</summary>
    private const string NpgsqlInstrumentationName = "Npgsql";

    /// <summary>
    /// Explicit buckets (seconds) for <c>http.server.request.duration</c>, so PromQL
    /// <c>histogram_quantile</c> works the same regardless of library defaults.
    /// </summary>
    private static readonly double[] HttpDurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    /// <summary>Registers metrics (Prometheus) and tracing (OTLP when configured).</summary>
    public static IServiceCollection AddM3ATelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEnabled = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(DefaultServiceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(NpgsqlInstrumentationName)
                .AddView(
                    "http.server.request.duration",
                    new ExplicitBucketHistogramConfiguration { Boundaries = HttpDurationBuckets })
                .AddPrometheusExporter());

        if (otlpEnabled)
        {
            // Npgsql spans are children of the active request span, so a trace shows each
            // REST call with the SQL it executed.
            telemetry.WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !IsInfrastructurePath(context.Request.Path))
                .AddSource(NpgsqlInstrumentationName)
                .AddOtlpExporter());
        }

        return services;
    }

    /// <summary>Maps the Prometheus scraping endpoint at <see cref="MetricsPath"/>.</summary>
    public static IEndpointRouteBuilder MapM3AMetrics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPrometheusScrapingEndpoint(MetricsPath);
        return endpoints;
    }

    /// <summary>Health probes and scrapes: high-volume, uninteresting traces.</summary>
    internal static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments(HealthCheckExtensions.BasePath)
        || path.StartsWithSegments(MetricsPath);
}
