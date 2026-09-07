namespace RecoveryApp.Domain.Entities;

/// <summary>An authenticated account. Created on first successful Sign in with Apple exchange.</summary>
public sealed class User
{
    /// <summary>Server generated, time ordered (UUIDv7) primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Email claim from the identity provider. Apple only releases this on first sign-in.</summary>
    public string? Email { get; set; }

    /// <summary>The stable <c>sub</c> claim of the Apple identity token. Unique per app team.</summary>
    public required string AppleUserId { get; set; }

    /// <summary>When the account row was first written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the account row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Tombstone marker for account deletion requests. Null for live accounts.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user's single settings row.</summary>
    public UserSettings? Settings { get; set; }
}
