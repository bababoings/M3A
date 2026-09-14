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

        services.AddDbContext<M3ADbContext>(options => options.UseNpgsql(connectionString));

        return services.AddRepositoryImplementations();
    }

    /// <summary>
    /// Adds the repositories without a <see cref="DbContext"/> provider, so tests can supply their own.
    /// </summary>
    public static IServiceCollection AddRepositoryImplementations(this IServiceCollection services)
    {
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        return services;
    }
}
