namespace M3A.Api.Extensions;

/// <summary>Titles used in every ProblemDetails response, kept in one place for consistency.</summary>
public static class ProblemTitles
{
    /// <summary>Request failed validation (HTTP 400).</summary>
    public const string ValidationFailed = "Validation Failed";

    /// <summary>Resource does not exist (HTTP 404).</summary>
    public const string NotFound = "Resource Not Found";

    /// <summary>Request is well formed but breaks a business rule (HTTP 422).</summary>
    public const string BusinessRuleViolation = "Business Rule Violation";

    /// <summary>Unhandled failure (HTTP 500).</summary>
    public const string InternalServerError = "Internal Server Error";
}
