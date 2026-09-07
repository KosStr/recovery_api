using System.Security.Claims;

namespace RecoveryApp.Application.Abstractions;

/// <summary>Issues and validates the application's own access and refresh tokens.</summary>
public interface ITokenService
{
    /// <summary>Mints a signed JWT access token for the given account.</summary>
    /// <param name="userId">The account the token is issued for.</param>
    /// <returns>The encoded token and its absolute expiry.</returns>
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(Guid userId);

    /// <summary>Mints an opaque refresh token together with the hash that gets persisted.</summary>
    /// <returns>The client-facing token, its stored hash, and its absolute expiry.</returns>
    (string Token, string TokenHash, DateTimeOffset ExpiresAt) CreateRefreshToken();

    /// <summary>Hashes a refresh token so it can be looked up against stored hashes.</summary>
    /// <param name="token">The client-supplied refresh token.</param>
    /// <returns>The Base64 encoded SHA-256 hash.</returns>
    string HashRefreshToken(string token);

    /// <summary>The claim type carrying the account id in issued access tokens.</summary>
    static string UserIdClaimType => ClaimTypes.NameIdentifier;
}
