using System.ComponentModel.DataAnnotations;

namespace RecoveryApp.Infrastructure.Auth;

/// <summary>Signing and lifetime settings for the tokens this service issues.</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>The <c>iss</c> claim written into issued access tokens.</summary>
    [Required]
    public string Issuer { get; set; } = "https://api.recoveryapp.local";

    /// <summary>The <c>aud</c> claim written into issued access tokens.</summary>
    [Required]
    public string Audience { get; set; } = "recoveryapp-mobile";

    /// <summary>
    /// HMAC-SHA256 signing key. At least 32 bytes. Supply it through user secrets, the environment
    /// or a secret manager; the development default in appsettings is not safe for deployment.
    /// </summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an issued access token stays valid. Short by design: an access token cannot be
    /// revoked, so its lifetime is the window an leaked one stays useful.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an issued refresh token stays valid.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(60);
}

/// <summary>Settings for the Sign in with Apple exchange.</summary>
public sealed class AppleAuthOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "AppleAuth";

    /// <summary>The bundle identifier Apple issues identity tokens for; the expected <c>aud</c> claim.</summary>
    public string ClientId { get; set; } = "com.recoveryapp.mobile";

    /// <summary>The expected <c>iss</c> claim.</summary>
    public string Issuer { get; set; } = "https://appleid.apple.com";

    /// <summary>
    /// Apple's OpenID Connect discovery document. It points at the JWKS endpoint
    /// (<c>https://appleid.apple.com/auth/keys</c>) that identity tokens are verified against.
    /// </summary>
    public string MetadataAddress { get; set; } = "https://appleid.apple.com/.well-known/openid-configuration";

    /// <summary>How long the fetched signing keys are reused before a scheduled refresh.</summary>
    public TimeSpan KeyCacheDuration { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// When true, identity tokens are decoded but their signature is <b>not</b> checked against
    /// Apple's JWKS, and any non-JWT string is accepted as a test identity. Local development and
    /// contract tests only — see <see cref="StubAppleIdentityTokenVerifier"/>.
    /// </summary>
    public bool UseStubVerification { get; set; }
}
