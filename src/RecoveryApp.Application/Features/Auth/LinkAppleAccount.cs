using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Auth;

/// <summary>Attaches a verified Apple identity to the calling guest account.</summary>
/// <param name="Request">The client payload.</param>
public sealed record LinkAppleAccountCommand(LinkAppleRequest Request) : IRequest<LinkAppleResponse>;

/// <summary>Validates <see cref="LinkAppleRequest"/>.</summary>
public sealed class LinkAppleRequestValidator : AbstractValidator<LinkAppleRequest>
{
    /// <summary>Initializes a new instance of the <see cref="LinkAppleRequestValidator"/> class.</summary>
    public LinkAppleRequestValidator()
    {
        RuleFor(x => x.IdentityToken)
            .NotEmpty()
            .MaximumLength(4096);

        RuleFor(x => x.FullName)
            .MaximumLength(256);
    }
}

/// <summary>
/// Handles <see cref="LinkAppleAccountCommand"/>.
/// </summary>
/// <remarks>
/// <para>Three outcomes, decided by what the Apple identity is already attached to:</para>
/// <list type="number">
/// <item><description>
/// <b>Nobody owns it</b> — the guest is promoted in place. Same account id, same rows, no migration.
/// </description></item>
/// <item><description>
/// <b>The caller already owns it</b> — a replayed request. Returns fresh tokens and reports no merge,
/// so a client retrying after a dropped response does not get an error.
/// </description></item>
/// <item><description>
/// <b>Another account owns it</b> — the registered account is authoritative. The guest's rows are
/// migrated onto it, the guest is tombstoned, and the caller is handed credentials for the surviving
/// account. This is the merge branch of the business rule: preserve the registered account, carry the
/// guest's data across.
/// </description></item>
/// </list>
/// <para>
/// Linking a second Apple identity onto an already-registered account is refused with a conflict
/// rather than merged — re-pointing a registration would silently hand one person's history to
/// another.
/// </para>
/// </remarks>
/// <param name="db">Persistence surface.</param>
/// <param name="verifier">Apple identity token verifier.</param>
/// <param name="issuer">Credential issuance.</param>
/// <param name="currentUser">Caller identity.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class LinkAppleAccountHandler(
    IAppDbContext db,
    IAppleIdentityTokenVerifier verifier,
    AuthTokenIssuer issuer,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<LinkAppleAccountCommand, LinkAppleResponse>
{
    /// <inheritdoc />
    public async Task<LinkAppleResponse> Handle(LinkAppleAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        AppleIdentity identity = await verifier
            .VerifyAsync(command.Request.IdentityToken, cancellationToken)
            .ConfigureAwait(false);

        Guid callerId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        User caller = await db.Users
            .FirstOrDefaultAsync(u => u.Id == callerId && u.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthenticationFailedException("The account is no longer active.");

        if (caller.AppleUserId is { } linked)
        {
            return string.Equals(linked, identity.AppleUserId, StringComparison.Ordinal)
                ? await AlreadyLinkedAsync(caller, now, cancellationToken).ConfigureAwait(false)
                : throw new AccountConflictException(
                    "This account is already linked to a different Apple ID. Sign out and sign in with Apple instead.");
        }

        User? owner = await db.Users
            .FirstOrDefaultAsync(
                u => u.AppleUserId == identity.AppleUserId && u.DeletedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        return owner is null
            ? await PromoteInPlaceAsync(caller, identity, now, cancellationToken).ConfigureAwait(false)
            : await MergeIntoAsync(caller, owner, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LinkAppleResponse> AlreadyLinkedAsync(
        User caller,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        caller.RecordLogin(now);

        AuthTokensResponse tokens = issuer.Issue(caller, now, isNewUser: false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LinkAppleResponse(tokens, Merged: false, AbsorbedUserId: null, Migrated: null);
    }

    private async Task<LinkAppleResponse> PromoteInPlaceAsync(
        User caller,
        AppleIdentity identity,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        caller.LinkApple(identity.AppleUserId, identity.Email, now);

        AuthTokensResponse tokens = issuer.Issue(caller, now, isNewUser: false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LinkAppleResponse(tokens, Merged: false, AbsorbedUserId: null, Migrated: null);
    }

    /// <summary>
    /// Moves everything the guest owns onto the registered account and retires the guest.
    /// </summary>
    /// <remarks>
    /// Rows are re-pointed through the change tracker rather than <c>ExecuteUpdate</c> so the whole
    /// merge commits as one transaction with the tombstone and the token issuance. That costs a load
    /// of the guest's rows, which is acceptable precisely because a guest account is pre-registration
    /// and therefore small. If guest lifetimes ever grow, move this to a set-based update inside an
    /// explicit transaction.
    ///
    /// The guest's settings row is deliberately left behind: the registered account is the one being
    /// preserved, so its own configuration wins.
    /// </remarks>
    private async Task<LinkAppleResponse> MergeIntoAsync(
        User guest,
        User owner,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!guest.IsAnonymous)
        {
            throw new AccountConflictException(
                "That Apple ID already belongs to another account, and this account is not a guest.");
        }

        List<UserSession> sessions = await db.UserSessions
            .Where(s => s.UserId == guest.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<EnergyCheckin> checkins = await db.EnergyCheckins
            .Where(c => c.UserId == guest.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<SyncChangelog> changelog = await db.SyncChangelogs
            .Where(c => c.UserId == guest.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (UserSession session in sessions)
        {
            session.UserId = owner.Id;

            // The row is new to the surviving account's devices, so it has to fall after their
            // current sync cursor or the next pull would never deliver it.
            session.UpdatedAt = now;
        }

        foreach (EnergyCheckin checkin in checkins)
        {
            checkin.UserId = owner.Id;
            checkin.UpdatedAt = now;
        }

        foreach (SyncChangelog entry in changelog)
        {
            entry.UserId = owner.Id;
        }

        await RevokeRefreshTokensAsync(guest.Id, now, cancellationToken).ConfigureAwait(false);

        guest.SoftDelete(now);
        owner.Email ??= guest.Email;
        owner.RecordLogin(now);

        AuthTokensResponse tokens = issuer.Issue(owner, now, isNewUser: false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new LinkAppleResponse(
            tokens,
            Merged: true,
            AbsorbedUserId: guest.Id,
            Migrated: new AccountMergeSummary(sessions.Count, checkins.Count, changelog.Count));
    }

    private async Task RevokeRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<RefreshToken> live = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (RefreshToken token in live)
        {
            token.RevokedAt = now;
        }
    }
}
