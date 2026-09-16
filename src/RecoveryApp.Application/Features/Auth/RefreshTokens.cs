using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Auth;

/// <summary>Rotates a refresh token into a fresh access and refresh token pair.</summary>
/// <param name="Request">The client payload.</param>
public sealed record RefreshTokensCommand(RefreshTokenRequest Request) : IRequest<AuthTokensResponse>;

/// <summary>Validates <see cref="RefreshTokenRequest"/>.</summary>
public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    /// <summary>Initializes a new instance of the <see cref="RefreshTokenRequestValidator"/> class.</summary>
    public RefreshTokenRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

/// <summary>Handles <see cref="RefreshTokensCommand"/>.</summary>
/// <param name="db">Persistence surface.</param>
/// <param name="tokens">Token service, used to hash the presented token for lookup.</param>
/// <param name="issuer">Credential issuance.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class RefreshTokensHandler(
    IAppDbContext db,
    ITokenService tokens,
    AuthTokenIssuer issuer,
    TimeProvider timeProvider)
    : IRequestHandler<RefreshTokensCommand, AuthTokensResponse>
{
    /// <inheritdoc />
    public async Task<AuthTokensResponse> Handle(RefreshTokensCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = timeProvider.GetUtcNow();
        string hash = tokens.HashRefreshToken(command.Request.RefreshToken);

        RefreshToken stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthenticationFailedException("The refresh token is not recognised.");

        if (stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            throw new AuthenticationFailedException("The refresh token has expired or has already been used.");
        }

        User user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == stored.UserId && u.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthenticationFailedException("The account is no longer active.");

        stored.RevokedAt = now;
        user.RecordLogin(now);

        AuthTokensResponse response = issuer.Issue(user, now, isNewUser: false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }
}
