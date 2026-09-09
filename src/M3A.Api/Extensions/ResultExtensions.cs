using M3A.Domain.ValueObjects;

namespace M3A.Api.Extensions;

/// <summary>Shared result shapes so every route reports the same errors the same way.</summary>
public static class ResultExtensions
{
    /// <summary>HTTP 400 for an identifier that does not match <see cref="ResourceId.Pattern"/>.</summary>
    public static IResult InvalidIdProblem(string parameterName, string value) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [parameterName] = [$"'{value}' does not match the required format {ResourceId.Pattern}."],
            },
            title: ProblemTitles.ValidationFailed);

    /// <summary>HTTP 404 for a resource that does not exist.</summary>
    public static IResult NotFoundProblem(string entityName, string id) =>
        TypedResults.Problem(
            detail: $"{entityName} '{id}' was not found.",
            statusCode: StatusCodes.Status404NotFound,
            title: ProblemTitles.NotFound);
}
