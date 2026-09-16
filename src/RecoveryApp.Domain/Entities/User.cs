namespace RecoveryApp.Domain.Entities;

/// <summary>
/// An account. Created either by a Sign in with Apple exchange or, for a first-time user who has not
/// registered yet, as an anonymous guest that can be upgraded in place later.
/// </summary>
public sealed class User
{
    /// <summary>Server generated, time ordered (UUIDv7) primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Email claim from the identity provider. Apple only releases this on first sign-in.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// The stable <c>sub</c> claim of the Apple identity token, unique per app team. Null while the
    /// account is an anonymous guest.
    /// </summary>
    public string? AppleUserId { get; set; }

    /// <summary>
    /// Whether the account is an unregistered guest. A guest owns real data and a real session; it
    /// simply has no identity provider behind it yet.
    /// </summary>
    public bool IsAnonymous { get; set; }

    /// <summary>When the account row was first written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the account row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The most recent successful token exchange. Null until the first one completes.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Tombstone marker for account deletion requests. Null for live accounts.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user's single settings row.</summary>
    public UserSettings? Settings { get; set; }

    /// <summary>Creates a registered account from a verified Apple identity.</summary>
    /// <param name="appleUserId">The verified <c>sub</c> claim.</param>
    /// <param name="email">The <c>email</c> claim, when Apple released one.</param>
    /// <param name="now">Server clock.</param>
    /// <returns>The new account.</returns>
    public static User CreateFromApple(string appleUserId, string? email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appleUserId);

        return new User
        {
            Id = Guid.CreateVersion7(now),
            AppleUserId = appleUserId,
            Email = email,
            IsAnonymous = false,
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now,
        };
    }

    /// <summary>Creates an anonymous guest account.</summary>
    /// <param name="now">Server clock.</param>
    /// <returns>The new guest account.</returns>
    public static User CreateAnonymous(DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        AppleUserId = null,
        Email = null,
        IsAnonymous = true,
        CreatedAt = now,
        UpdatedAt = now,
        LastLoginAt = now,
    };

    /// <summary>
    /// Attaches a verified Apple identity to a guest account, promoting it to a registered one.
    /// </summary>
    /// <param name="appleUserId">The verified <c>sub</c> claim.</param>
    /// <param name="email">The <c>email</c> claim, when Apple released one.</param>
    /// <param name="now">Server clock.</param>
    /// <exception cref="InvalidOperationException">
    /// The account already carries an Apple identity. Re-pointing an existing registration at a
    /// different Apple account would silently hand one user's data to another, so it is refused.
    /// </exception>
    public void LinkApple(string appleUserId, string? email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appleUserId);

        if (AppleUserId is not null)
        {
            throw new InvalidOperationException($"Account {Id} is already linked to an Apple identity.");
        }

        AppleUserId = appleUserId;
        Email ??= email;
        IsAnonymous = false;
        UpdatedAt = now;
        LastLoginAt = now;
    }

    /// <summary>Records a successful token exchange.</summary>
    /// <param name="now">Server clock.</param>
    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        UpdatedAt = now;
    }

    /// <summary>Tombstones the account after its data has been migrated elsewhere.</summary>
    /// <param name="now">Server clock.</param>
    public void SoftDelete(DateTimeOffset now)
    {
        DeletedAt = now;
        UpdatedAt = now;
    }
}
