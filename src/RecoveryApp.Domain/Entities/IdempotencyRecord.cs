namespace RecoveryApp.Domain.Entities;

/// <summary>
/// Caches the outcome of a mutating request that carried an <c>Idempotency-Key</c> header, so a
/// retry from a flaky mobile connection replays the original response instead of re-applying work.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Owning user. Keys are scoped per user so they cannot collide across accounts.</summary>
    public Guid UserId { get; set; }

    /// <summary>The client supplied <c>Idempotency-Key</c> header value.</summary>
    public required string Key { get; set; }

    /// <summary>Request path the key was first used against, to reject key reuse across endpoints.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Base64 encoded SHA-256 hash of the original request body.</summary>
    public required string RequestHash { get; set; }

    /// <summary>The serialized response body that was returned the first time.</summary>
    public required string ResponseBody { get; set; }

    /// <summary>The HTTP status code that was returned the first time.</summary>
    public int StatusCode { get; set; }

    /// <summary>When the record was written.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
