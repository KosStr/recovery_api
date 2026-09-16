using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;

namespace RecoveryApp.Infrastructure.Auth;

/// <summary>
/// Development stand-in for Apple identity token verification.
/// </summary>
/// <remarks>
/// <para>
/// It decodes the token and reads the <c>sub</c>, <c>email</c> and <c>email_verified</c> claims but
/// performs <b>no signature check</b>. Any non-JWT string is accepted as well and folded into a
/// deterministic pseudo subject, so a mobile developer can sign in from the simulator with
/// <c>"dev-alice"</c> before the Apple developer account is wired up.
/// </para>
/// <para>
/// Before deployment replace this with a verifier that fetches
/// <c>https://appleid.apple.com/auth/keys</c>, caches the JWKS, and validates the signature, the
/// <c>iss</c> claim against <see cref="AppleAuthOptions.Issuer"/>, the <c>aud</c> claim against
/// <see cref="AppleAuthOptions.ClientId"/> and the expiry. Registration is guarded by
/// <see cref="AppleAuthOptions.UseStubVerification"/>.
/// </para>
/// </remarks>
/// <param name="options">Apple exchange settings.</param>
/// <param name="logger">Logger used to make the stub impossible to miss in the logs.</param>
public sealed class StubAppleIdentityTokenVerifier(
    IOptions<AppleAuthOptions> options,
    ILogger<StubAppleIdentityTokenVerifier> logger)
    : IAppleIdentityTokenVerifier
{
    private readonly AppleAuthOptions _options = options.Value;

    /// <inheritdoc />
    public Task<AppleIdentity> VerifyAsync(string identityToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identityToken))
        {
            throw new AuthenticationFailedException("The identity token is missing.");
        }

        logger.LogWarning(
            "Apple identity token accepted without signature verification. This stub must not run in production.");

        if (!TryReadJwt(identityToken, out AppleIdentity identity))
        {
            identity = new AppleIdentity(DeterministicSubject(identityToken), Email: null, IsEmailVerified: false);
        }

        return Task.FromResult(identity);
    }

    private bool TryReadJwt(string identityToken, out AppleIdentity identity)
    {
        identity = default;

        JsonWebTokenHandler handler = new();

        if (!handler.CanReadToken(identityToken))
        {
            return false;
        }

        JsonWebToken token = handler.ReadJsonWebToken(identityToken);

        if (!token.TryGetClaim("sub", out var subject) || string.IsNullOrWhiteSpace(subject.Value))
        {
            return false;
        }

        if (token.Issuer.Length > 0 && !string.Equals(token.Issuer, _options.Issuer, StringComparison.Ordinal))
        {
            throw new AuthenticationFailedException("The identity token was issued by an unexpected party.");
        }

        string? email = token.TryGetClaim("email", out var emailClaim) ? emailClaim.Value : null;
        bool verified = token.TryGetClaim("email_verified", out var verifiedClaim)
            && bool.TryParse(verifiedClaim.Value, out bool parsed)
            && parsed;

        identity = new AppleIdentity(subject.Value, email, verified);
        return true;
    }

    private static string DeterministicSubject(string token) =>
        $"stub.{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)).AsSpan(0, 12))}";
}
