using M3A.Repositories.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace M3A.Api.Tests;

/// <summary>
/// Boots the real API with the PostgreSQL context swapped for a per-factory in-memory
/// database, so integration tests need no external infrastructure.
/// </summary>
public sealed class M3AApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"m3a-api-{Guid.NewGuid():N}";

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // A literal connection string with no ${...} placeholders, so the suite never depends
        // on a .env file existing. The Npgsql registration it produces is removed below anyway.
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            "Host=localhost;Database=m3a_test;Username=test;Password=test");

        builder.ConfigureServices(services =>
        {
            // EF Core registers the provider through IDbContextOptionsConfiguration<T> as well;
            // all three must go or both providers end up in the same service provider.
            services.RemoveAll<IDbContextOptionsConfiguration<M3ADbContext>>();
            services.RemoveAll<DbContextOptions<M3ADbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<M3ADbContext>();

            services.AddDbContext<M3ADbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
