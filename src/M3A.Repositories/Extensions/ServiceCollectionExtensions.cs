using M3A.Repositories.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace M3A.Repositories.Extensions;

/// <summary>Registers the persistence layer with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the connection string read from configuration.</summary>
    public const string ConnectionStringName = "DefaultConnection";

    /// <summary>
    /// Npgsql data source name: the <c>db.client.connection.pool.name</c> label on Npgsql
    /// metrics. Without it Npgsql uses the connection string, leaking connection details.
    /// </summary>
    public const string DataSourceName = "m3a-db";

    /// <summary>Adds the <see cref="M3ADbContext"/> and one repository per aggregate root.</summary>
    public static IServiceCollection AddRepositories(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var template = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");

        var connectionString = ConnectionStringTemplate.Expand(
            template, Environment.GetEnvironmentVariable);

        services.AddDbContext<M3ADbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.ConfigureDataSource(dataSource => dataSource.Name = DataSourceName)));

        return services.AddRepositoryImplementations();
    }

    /// <summary>
    /// Adds the repositories without a <see cref="DbContext"/> provider, so tests can supply their own.
    /// </summary>
    public static IServiceCollection AddRepositoryImplementations(this IServiceCollection services)
    {
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        return services;
    }
}
