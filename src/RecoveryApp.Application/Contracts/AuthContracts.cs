namespace RecoveryApp.Application.Contracts;

/// <summary>Exchanges a Sign in with Apple identity token for application tokens.</summary>
/// <param name="IdentityToken">The raw JWT from <c>ASAuthorizationAppleIDCredential.identityToken</c>.</param>
/// <param name="AuthorizationCode">Apple's single-use authorization code, if the client captured one.</param>
/// <param name="FullName">Display name, released by Apple only on the very first authorization.</param>
public sealed record AppleSignInRequest(
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
public sealed record UserProfileResponse(
    Guid Id,
    string? Email,
    DateTimeOffset CreatedAt,
    bool IsNewUser);
