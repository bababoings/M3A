using Microsoft.Extensions.DependencyInjection;

namespace M3A.Delegates.Extensions;

/// <summary>Registers the delegate layer with the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds one delegate per domain.</summary>
    public static IServiceCollection AddDelegates(this IServiceCollection services)
    {
        services.AddScoped<IItemDelegate, ItemDelegate>();
        services.AddScoped<IVenueDelegate, VenueDelegate>();
        return services;
    }
}
