namespace M3A.Domain.Exceptions;

/// <summary>Base type for every error the domain raises deliberately.</summary>
public abstract class DomainException(string message) : Exception(message);
