namespace M3A.Domain.Exceptions;

/// <summary>Raised when an entity is addressed by an id that does not exist. Surfaces as HTTP 404.</summary>
public sealed class EntityNotFoundException(string entityName, string id)
    : DomainException($"{entityName} '{id}' was not found.")
{
    /// <summary>Name of the entity type that was searched.</summary>
    public string EntityName { get; } = entityName;

    /// <summary>The identifier that produced no match.</summary>
    public string Id { get; } = id;
}
