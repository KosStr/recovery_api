using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Auth;

/// <summary>Exchanges a verified Apple identity token for application tokens, creating the account on first use.</summary>
/// <param name="Request">The client payload.</param>
public sealed record SignInWithAppleCommand(AppleSignInRequest Request) : IRequest<AuthTokensResponse>;

/// <summary>Validates <see cref="AppleSignInRequest"/>.</summary>
public sealed class AppleSignInRequestValidator : AbstractValidator<AppleSignInRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AppleSignInRequestValidator"/> class.</summary>
    public AppleSignInRequestValidator()
    {
        RuleFor(x => x.IdentityToken)
            .NotEmpty()
            .MaximumLength(4096);

        RuleFor(x => x.FullName)
            .MaximumLength(256);
    }
}

/// <summary>Handles <see cref="SignInWithAppleCommand"/>.</summary>
/// <param name="db">Persistence surface.</param>
/// <param name="verifier">Apple identity token verifier.</param>
/// <param name="tokens">Application token service.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class SignInWithAppleHandler(
    IAppDbContext db,
    IAppleIdentityTokenVerifier verifier,
    ITokenService tokens,
    TimeProvider timeProvider)
    : IRequestHandler<SignInWithAppleCommand, AuthTokensResponse>
{
    /// <inheritdoc />
    public async Task<AuthTokensResponse> Handle(SignInWithAppleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        AppleIdentity identity = await verifier
            .VerifyAsync(command.Request.IdentityToken, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();

        User? user = await db.Users
            .FirstOrDefaultAsync(u => u.AppleUserId == identity.AppleUserId && u.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        bool isNewUser = user is null;

        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(now),
                AppleUserId = identity.AppleUserId,
                Email = identity.Email,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Users.Add(user);
            db.UserSettings.Add(new UserSettings { UserId = user.Id, UpdatedAt = now });
        }
        else if (identity.Email is not null && user.Email != identity.Email)
        {
            // Apple only releases the email on the first authorization; keep the first value we saw
            // unless the account genuinely has none yet.
            user.Email ??= identity.Email;
            user.UpdatedAt = now;
        }

        (string accessToken, DateTimeOffset accessExpiresAt) = tokens.CreateAccessToken(user.Id);
        (string refreshToken, string refreshHash, DateTimeOffset refreshExpiresAt) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AuthTokensResponse(
            accessToken,
            refreshToken,
            accessExpiresAt,
            refreshExpiresAt,
            "Bearer",
            new UserProfileResponse(user.Id, user.Email, user.CreatedAt, isNewUser));
    }
}
