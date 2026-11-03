using M3A.Repositories.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace M3A.Api.Extensions;

/// <summary>
/// Health endpoints for orchestrators (Kubernetes probes, load balancers):
/// <list type="bullet">
///   <item><c>GET /health/live</c> — the process is up and serving HTTP. Runs no checks, so a
///   database outage never makes the orchestrator restart healthy API instances.</item>
///   <item><c>GET /health/ready</c> — the instance can do useful work: the database is
///   reachable. <c>503</c> takes the instance out of rotation until it recovers.</item>
/// </list>
/// Both answer <c>200 Healthy</c> or <c>503 Unhealthy</c> as plain text.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>Base path shared by the health endpoints.</summary>
    public const string BasePath = "/health";

    /// <summary>Tag selecting the checks that gate readiness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Registers the readiness checks.</summary>
    public static IServiceCollection AddM3AHealthChecks(this IServiceCollection services)
    {
        // Opens a connection through the same DbContext the repositories use, so it follows
        // whatever provider is registered (Npgsql in the app, InMemory in tests).
        services.AddHealthChecks()
            .AddDbContextCheck<M3ADbContext>("database", tags: [ReadyTag]);

        return services;
    }

    /// <summary>Maps <c>/health/live</c> and <c>/health/ready</c>.</summary>
    public static IEndpointRouteBuilder MapM3AHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks($"{BasePath}/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        endpoints.MapHealthChecks($"{BasePath}/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
        });

        return endpoints;
    }
}
