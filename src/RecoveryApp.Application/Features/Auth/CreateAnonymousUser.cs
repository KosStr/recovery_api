using MediatR;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Auth;

/// <summary>
/// Creates a guest account so a first-time user can start recording sessions immediately, with no
/// registration step in front of the product.
/// </summary>
public sealed record CreateAnonymousUserCommand : IRequest<AuthTokensResponse>;

/// <summary>Handles <see cref="CreateAnonymousUserCommand"/>.</summary>
/// <param name="db">Persistence surface.</param>
/// <param name="issuer">Credential issuance.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class CreateAnonymousUserHandler(IAppDbContext db, AuthTokenIssuer issuer, TimeProvider timeProvider)
    : IRequestHandler<CreateAnonymousUserCommand, AuthTokensResponse>
{
    /// <inheritdoc />
    public async Task<AuthTokensResponse> Handle(CreateAnonymousUserCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        User user = User.CreateAnonymous(now);

        db.Users.Add(user);

        // Provisioned here for the same reason as on Apple sign-in: a guest is a real account, and
        // every settings read after this must find a row.
        db.UserSettings.Add(new UserSettings { UserId = user.Id, UpdatedAt = now });

        AuthTokensResponse response = issuer.Issue(user, now, isNewUser: true);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }
}
