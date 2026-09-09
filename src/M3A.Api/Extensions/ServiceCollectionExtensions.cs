using System.Text.Json.Serialization;
using M3A.Api.Middleware;
using M3A.Delegates.Extensions;
using M3A.Repositories.Extensions;
using FluentValidation;

namespace M3A.Api.Extensions;

/// <summary>Composition root: wires every layer into the DI container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers API, delegate and repository services.</summary>
    public static IServiceCollection AddM3AApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddApiServices();
        services.AddDelegates();
        services.AddRepositories(configuration);
        return services;
    }

    /// <summary>
    /// Registers only the API layer concerns: serialization, validation, problem details, OpenAPI.
    /// Split out so tests can compose their own persistence.
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path);

        services.AddValidatorsFromAssembly(typeof(ServiceCollectionExtensions).Assembly, includeInternalTypes: true);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();

        return services;
    }
}
