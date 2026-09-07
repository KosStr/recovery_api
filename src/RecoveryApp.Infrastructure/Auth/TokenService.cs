using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RecoveryApp.Application.Abstractions;

namespace RecoveryApp.Infrastructure.Auth;

/// <summary>Issues HMAC-signed access tokens and opaque, hash-at-rest refresh tokens.</summary>
/// <param name="options">Signing and lifetime settings.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    /// <inheritdoc />
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(Guid userId)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now + _options.AccessTokenLifetime;

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7(now).ToString()),
            ]),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }

    /// <inheritdoc />
    public (string Token, string TokenHash, DateTimeOffset ExpiresAt) CreateRefreshToken()
    {
        // 256 bits of entropy, URL-safe so it survives being stored in the iOS keychain and sent
        // back in a JSON body without escaping.
        string token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        return (token, HashRefreshToken(token), timeProvider.GetUtcNow() + _options.RefreshTokenLifetime);
    }

    /// <inheritdoc />
    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
