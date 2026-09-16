using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;

namespace RecoveryApp.Infrastructure.Auth;

/// <summary>
/// Verifies a Sign in with Apple identity token against Apple's published signing keys.
/// </summary>
/// <remarks>
/// <para>
/// Keys come from Apple's OpenID Connect discovery document, which points at
/// <c>https://appleid.apple.com/auth/keys</c>. <see cref="ConfigurationManager{T}"/> owns the fetch,
/// the cache and the refresh, so key rollover is handled without a deployment: when Apple signs with
/// a <c>kid</c> we have not seen, the manager re-fetches rather than failing the sign-in.
/// </para>
/// <para>
/// Everything is checked: signature, <c>iss</c>, <c>aud</c> and lifetime. A token that fails any of
/// them raises <see cref="AuthenticationFailedException"/>, which the API surfaces as
/// <c>401</c> with a problem document.
/// </para>
/// </remarks>
/// <param name="configurationManager">Caching retriever for Apple's OIDC metadata and JWKS.</param>
/// <param name="options">Apple exchange settings.</param>
/// <param name="logger">Logger.</param>
public sealed class AppleJwksIdentityTokenVerifier(
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager,
    IOptions<AppleAuthOptions> options,
    ILogger<AppleJwksIdentityTokenVerifier> logger)
    : IAppleIdentityTokenVerifier
{
    private readonly AppleAuthOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    /// <inheritdoc />
    public async Task<AppleIdentity> VerifyAsync(string identityToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identityToken))
        {
            throw new AuthenticationFailedException("The identity token is missing.");
        }

        OpenIdConnectConfiguration configuration;

        try
        {
            configuration = await configurationManager
                .GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Apple being unreachable is an upstream outage, not a bad credential: do not tell the
            // user their token is invalid when we simply could not check it.
            logger.LogError(exception, "Could not retrieve Apple's signing keys.");
            throw new InvalidOperationException("Apple's signing keys are currently unavailable.", exception);
        }

        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        };

        TokenValidationResult result = await _handler
            .ValidateTokenAsync(identityToken, parameters)
            .ConfigureAwait(false);

        if (!result.IsValid)
        {
            logger.LogInformation(result.Exception, "Rejected an Apple identity token.");
            throw new AuthenticationFailedException(Describe(result.Exception));
        }

        JsonWebToken token = (JsonWebToken)result.SecurityToken;

        if (!token.TryGetClaim("sub", out Claim? subject) || string.IsNullOrWhiteSpace(subject.Value))
        {
            throw new AuthenticationFailedException("The identity token carries no subject claim.");
        }

        string? email = token.TryGetClaim("email", out Claim? emailClaim) ? emailClaim.Value : null;

        bool emailVerified = token.TryGetClaim("email_verified", out Claim? verifiedClaim)
            && bool.TryParse(verifiedClaim.Value, out bool parsed)
            && parsed;

        return new AppleIdentity(subject.Value, email, emailVerified);
    }

    private static string Describe(Exception? exception) => exception switch
    {
        SecurityTokenExpiredException => "The identity token has expired.",
        SecurityTokenInvalidAudienceException => "The identity token was issued for a different application.",
        SecurityTokenInvalidIssuerException => "The identity token was issued by an unexpected party.",
        SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException =>
            "The identity token signature could not be verified.",
        _ => "The identity token is not valid.",
    };
}
