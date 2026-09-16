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
/// <param name="issuer">Credential issuance.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class SignInWithAppleHandler(
    IAppDbContext db,
    IAppleIdentityTokenVerifier verifier,
    AuthTokenIssuer issuer,
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
            user = User.CreateFromApple(identity.AppleUserId, identity.Email, now);

            db.Users.Add(user);
            db.UserSettings.Add(new UserSettings { UserId = user.Id, UpdatedAt = now });
        }
        else
        {
            // Apple only releases the email on the first authorization, so a later sign-in carrying
            // one fills a gap rather than overwriting what we already trust.
            user.Email ??= identity.Email;
            user.RecordLogin(now);
        }

        AuthTokensResponse response = issuer.Issue(user, now, isNewUser);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }
}
