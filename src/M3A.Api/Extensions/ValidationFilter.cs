using FluentValidation;

namespace M3A.Api.Extensions;

/// <summary>
/// Endpoint filter that runs the registered FluentValidation validator for
/// <typeparamref name="TRequest"/> before the handler executes.
/// </summary>
/// <typeparam name="TRequest">The request DTO bound from the body.</typeparam>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is null)
        {
            return await next(context);
        }

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["body"] = ["A request body of type " + typeof(TRequest).Name + " is required."],
                },
                title: ProblemTitles.ValidationFailed);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        return TypedResults.ValidationProblem(
            result.ToDictionary(),
            title: ProblemTitles.ValidationFailed);
    }
}

/// <summary>Adds <see cref="ValidationFilter{TRequest}"/> to a route.</summary>
public static class ValidationFilterExtensions
{
    /// <summary>Validates the <typeparamref name="TRequest"/> body before the handler runs.</summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
