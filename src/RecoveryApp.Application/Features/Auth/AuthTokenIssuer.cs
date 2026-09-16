using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Auth;

/// <summary>
/// Mints the credential pair every auth endpoint returns and stages the refresh token for
/// persistence. Kept in one place so the four exchanges — Apple sign-in, anonymous sign-in, linking
/// and refresh — cannot drift on lifetimes, hashing or what ends up in the response.
/// </summary>
/// <param name="db">Persistence surface.</param>
/// <param name="tokens">Token service.</param>
public sealed class AuthTokenIssuer(IAppDbContext db, ITokenService tokens)
{
    /// <summary>
    /// Issues an access and refresh token for <paramref name="user"/> and adds the refresh token's
    /// hash to the change tracker. The caller owns the <c>SaveChanges</c>, so issuance joins the
    /// same transaction as whatever else the handler did.
    /// </summary>
    /// <param name="user">The account to authenticate.</param>
    /// <param name="now">Server clock.</param>
    /// <param name="isNewUser">Whether this exchange created the account.</param>
    /// <returns>The response body for the endpoint.</returns>
    public AuthTokensResponse Issue(User user, DateTimeOffset now, bool isNewUser)
    {
        ArgumentNullException.ThrowIfNull(user);

        (string accessToken, DateTimeOffset accessExpiresAt) = tokens.CreateAccessToken(user.Id, user.IsAnonymous);
        (string refreshToken, string refreshHash, DateTimeOffset refreshExpiresAt) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt,
        });

        return new AuthTokensResponse(
            accessToken,
            refreshToken,
            accessExpiresAt,
            refreshExpiresAt,
            "Bearer",
            new UserProfileResponse(user.Id, user.Email, user.CreatedAt, isNewUser, user.IsAnonymous, user.LastLoginAt));
    }
}
