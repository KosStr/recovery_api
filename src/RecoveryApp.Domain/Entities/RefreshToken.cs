namespace RecoveryApp.Domain.Entities;

/// <summary>
/// A persisted, single-use refresh token. Only the SHA-256 hash is stored so a database leak cannot
/// be replayed against the token endpoint.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Time ordered (UUIDv7) primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Base64 encoded SHA-256 hash of the opaque token handed to the client.</summary>
    public required string TokenHash { get; set; }

    /// <summary>When the token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the token was redeemed or revoked. Null while the token is live.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
