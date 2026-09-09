using M3A.Api.Extensions;
using M3A.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace M3A.Api.Middleware;

/// <summary>
/// Translates exceptions escaping the delegate layer into ProblemDetails responses.
/// This is the only place a status code is derived from an exception type.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = Translate(exception);

        if (problemDetails.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status}: {Message}",
                problemDetails.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails,
        });
    }

    private static ProblemDetails Translate(Exception exception) => exception switch
    {
        ValidationException validation => new ValidationProblemDetails(ToErrors(validation))
        {
            Title = ProblemTitles.ValidationFailed,
            Status = StatusCodes.Status400BadRequest,
        },
        EntityNotFoundException notFound => new ProblemDetails
        {
            Title = ProblemTitles.NotFound,
            Detail = notFound.Message,
            Status = StatusCodes.Status404NotFound,
        },
        BusinessRuleViolationException violation => new ProblemDetails
        {
            Title = ProblemTitles.BusinessRuleViolation,
            Detail = violation.Message,
            Status = StatusCodes.Status422UnprocessableEntity,
        },
        _ => new ProblemDetails
        {
            Title = ProblemTitles.InternalServerError,
            Status = StatusCodes.Status500InternalServerError,
        },
    };

    private static Dictionary<string, string[]> ToErrors(ValidationException exception) =>
        exception.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());
}
