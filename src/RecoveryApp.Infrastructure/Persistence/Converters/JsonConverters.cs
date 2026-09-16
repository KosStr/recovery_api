using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RecoveryApp.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a CLR object as a Postgres <c>jsonb</c> document. Serialization is pinned to a single
/// options instance so the on-disk shape is stable across processes.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
public sealed class JsonDocumentConverter<T> : ValueConverter<T, string>
    where T : class
{
    /// <summary>Initializes a new instance of the <see cref="JsonDocumentConverter{T}"/> class.</summary>
    public JsonDocumentConverter()
        : base(
            value => JsonSerializer.Serialize(value, JsonPersistence.Options),
            json => JsonSerializer.Deserialize<T>(json, JsonPersistence.Options)!)
    {
    }
}

/// <summary>
/// Change tracking for <c>jsonb</c> columns. EF cannot diff an arbitrary object graph, so equality
/// and hashing go through the serialized form and snapshots are deep copies.
/// </summary>
/// <typeparam name="T">The document type.</typeparam>
public sealed class JsonDocumentComparer<T> : ValueComparer<T>
    where T : class
{
    /// <summary>Initializes a new instance of the <see cref="JsonDocumentComparer{T}"/> class.</summary>
    public JsonDocumentComparer()
        : base(
            (left, right) => JsonSerializer.Serialize(left, JsonPersistence.Options)
                == JsonSerializer.Serialize(right, JsonPersistence.Options),
            value => JsonSerializer.Serialize(value, JsonPersistence.Options).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<T>(
                JsonSerializer.Serialize(value, JsonPersistence.Options),
                JsonPersistence.Options)!)
    {
    }
}

/// <summary>Serializer settings shared by every <c>jsonb</c> column.</summary>
public static class JsonPersistence
{
    /// <summary>The pinned serializer options used for persistence.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
}
