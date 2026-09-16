namespace RecoveryApp.Application.Common;

/// <summary>Raised when a handler cannot resolve an entity the caller addressed. Maps to HTTP 404.</summary>
public sealed class NotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="NotFoundException"/> class.</summary>
    /// <param name="entityName">CLR name of the entity that was not found.</param>
    /// <param name="key">The key that was looked up.</param>
    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.")
    {
        EntityName = entityName;
        Key = key;
    }

    /// <summary>CLR name of the entity that was not found.</summary>
    public string EntityName { get; }

    /// <summary>The key that was looked up.</summary>
    public object Key { get; }
}
