namespace M3A.Domain.Exceptions;

/// <summary>
/// Raised when a request is well formed but violates a business rule
/// (duplicate entity, invalid state transition, ...). Surfaces as HTTP 422.
/// </summary>
public sealed class BusinessRuleViolationException(string message) : DomainException(message);
