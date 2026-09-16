namespace RecoveryApp.Application.Contracts;

/// <summary>Exchanges a Sign in with Apple identity token for application tokens.</summary>
/// <param name="IdentityToken">The raw JWT from <c>ASAuthorizationAppleIDCredential.identityToken</c>.</param>
/// <param name="AuthorizationCode">Apple's single-use authorization code, if the client captured one.</param>
/// <param name="FullName">Display name, released by Apple only on the very first authorization.</param>
public sealed record AppleSignInRequest(
    string IdentityToken,
    string? AuthorizationCode = null,
    string? FullName = null);

/// <summary>Attaches an Apple identity to the currently authenticated guest account.</summary>
/// <param name="IdentityToken">The raw JWT from <c>ASAuthorizationAppleIDCredential.identityToken</c>.</param>
/// <param name="AuthorizationCode">Apple's single-use authorization code, if the client captured one.</param>
/// <param name="FullName">Display name, released by Apple only on the very first authorization.</param>
public sealed record LinkAppleRequest(
    string IdentityToken,
    string? AuthorizationCode = null,
    string? FullName = null);

/// <summary>Exchanges a refresh token for a new access token.</summary>
/// <param name="RefreshToken">The opaque refresh token from a previous exchange.</param>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>The credential pair returned by every successful token exchange.</summary>
/// <param name="AccessToken">Signed JWT to send as <c>Authorization: Bearer</c>.</param>
/// <param name="RefreshToken">Opaque single-use token used to obtain the next access token.</param>
/// <param name="ExpiresAt">Absolute expiry of <paramref name="AccessToken"/>.</param>
/// <param name="RefreshTokenExpiresAt">Absolute expiry of <paramref name="RefreshToken"/>.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="User">The authenticated account.</param>
public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType,
    UserProfileResponse User);

/// <summary>The public projection of an account.</summary>
/// <param name="Id">Account id.</param>
/// <param name="Email">Email, when Apple has released one.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="IsNewUser">Whether this exchange created the account.</param>
/// <param name="IsAnonymous">Whether the account is still an unregistered guest.</param>
/// <param name="LastLoginAt">The most recent successful token exchange.</param>
public sealed record UserProfileResponse(
    Guid Id,
    string? Email,
    DateTimeOffset CreatedAt,
    bool IsNewUser,
    bool IsAnonymous,
    DateTimeOffset? LastLoginAt);

/// <summary>The outcome of attaching an Apple identity to a guest account.</summary>
/// <param name="Tokens">Fresh credentials for the surviving account.</param>
/// <param name="Merged">
/// Whether the Apple identity already belonged to a registered account. When true the guest was
/// absorbed into that account and <paramref name="Tokens"/> authenticate the registered one, so the
/// client must replace every stored credential and its local user id.
/// </param>
/// <param name="AbsorbedUserId">The guest account that was merged away, when <paramref name="Merged"/> is true.</param>
/// <param name="Migrated">What moved across during the merge.</param>
public sealed record LinkAppleResponse(
    AuthTokensResponse Tokens,
    bool Merged,
    Guid? AbsorbedUserId,
    AccountMergeSummary? Migrated);

/// <summary>Row counts moved from a guest account into a registered one.</summary>
/// <param name="Sessions">Recovery sessions re-pointed at the surviving account.</param>
/// <param name="EnergyCheckins">Energy check-ins re-pointed at the surviving account.</param>
/// <param name="ChangelogEntries">Sync changelog entries re-pointed at the surviving account.</param>
public sealed record AccountMergeSummary(int Sessions, int EnergyCheckins, int ChangelogEntries);
