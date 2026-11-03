using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace M3A.Api.Tests.Routes;

/// <summary>Contract for <c>/health/live</c> and <c>/health/ready</c>.</summary>
public class HealthRoutesTests(M3AApiFactory factory) : M3AApiTests(factory)
{
    private readonly M3AApiFactory _factory = factory;

    [Fact]
    public async Task Live_Returns200Healthy()
    {
        var response = await Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_Returns200Healthy_WhenDatabaseIsReachable()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_Returns503_ButLiveStays200_WhenDatabaseIsUnreachable()
    {
        using var client = WithUnreachableDatabase().CreateClient();

        var ready = await client.GetAsync("/health/ready");
        var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    /// <summary>
    /// The real Npgsql provider pointed at a closed local port: the connection is refused
    /// immediately, so the check fails fast without any PostgreSQL instance.
    /// </summary>
    private WebApplicationFactory<Program> WithUnreachableDatabase() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<M3ADbContext>>();
            services.RemoveAll<DbContextOptions<M3ADbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<M3ADbContext>();

            services.AddDbContext<M3ADbContext>(options => options.UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2"));
        }));
}
