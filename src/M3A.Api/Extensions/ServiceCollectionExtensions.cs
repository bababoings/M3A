using System.Text.Json.Serialization;
using M3A.Api.Middleware;
using M3A.Delegates.Extensions;
using M3A.Repositories.Extensions;
using FluentValidation;
using Microsoft.OpenApi;

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
        services.AddOpenApi(options =>
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "M3A Ticketing API",
                    Version = "v1",
                    Description = "REST API for managing venues, events, and tickets. "
                        + "Resource ids match `^[A-Za-z0-9\\-]+$`. Errors are returned as RFC 9457 problem details.",
                };
                return Task.CompletedTask;
            }));

        return services;
    }
}
