using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Application.Features.Auth;
using RecoveryApp.Domain.Entities;
using RecoveryApp.Domain.Enums;
using RecoveryApp.Infrastructure.Auth;
using RecoveryApp.Infrastructure.Persistence;

namespace RecoveryApp.UnitTests;

/// <summary>Covers BE-101 (Apple sign-in) and BE-102 (guest accounts and identity linking).</summary>
public sealed class AuthHandlerTests : IDisposable
{
    private const string AppleSubject = "001234.abcdef.5678";

    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(Noon);
    private readonly StubVerifier _verifier = new(new AppleIdentity(AppleSubject, "alice@example.com", true));
    private readonly TestCurrentUser _currentUser = new(null);
    private readonly ITokenService _tokens;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AuthHandlerTests() =>
        _tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = new string('k', 48) }),
            _clock);

    public void Dispose() => _db.Dispose();

    // ---------- BE-101: Apple sign-in ----------

    [Fact]
    public async Task AppleSignIn_CreatesAccountAndDefaultSettings_OnFirstUse()
    {
        AuthTokensResponse response = await SignInAsync();

        User user = await _db.Users.SingleAsync(Ct);

        Assert.Equal(AppleSubject, user.AppleUserId);
        Assert.False(user.IsAnonymous);
        Assert.Equal("alice@example.com", user.Email);
        Assert.True(response.User.IsNewUser);
        Assert.Equal(user.Id, response.User.Id);

        // AC2: settings are provisioned automatically, not lazily on first read.
        Assert.True(await _db.UserSettings.AnyAsync(s => s.UserId == user.Id, Ct));
    }

    [Fact]
    public async Task AppleSignIn_UpdatesLastLoginAndReusesAccount_OnReturningUser()
    {
        AuthTokensResponse first = await SignInAsync();

        _clock.Advance(TimeSpan.FromHours(6));
        AuthTokensResponse second = await SignInAsync();

        // AC3: the same account comes back, with last_login_at moved forward.
        Assert.Equal(first.User.Id, second.User.Id);
        Assert.False(second.User.IsNewUser);
        Assert.Equal(1, await _db.Users.CountAsync(Ct));

        User user = await _db.Users.SingleAsync(Ct);
        Assert.Equal(Noon.AddHours(6), user.LastLoginAt);
    }

    [Fact]
    public async Task AppleSignIn_IssuesA15MinuteAccessToken()
    {
        // AC4: access tokens are short lived because they cannot be revoked.
        AuthTokensResponse response = await SignInAsync();

        Assert.Equal(Noon.AddMinutes(15), response.ExpiresAt);
        Assert.Equal(Noon.AddDays(60), response.RefreshTokenExpiresAt);
        Assert.Equal("Bearer", response.TokenType);
    }

    [Fact]
    public async Task AppleSignIn_StoresOnlyTheRefreshTokenHash()
    {
        // AC4: a database leak must not yield replayable refresh tokens.
        AuthTokensResponse response = await SignInAsync();

        RefreshToken stored = await _db.RefreshTokens.SingleAsync(Ct);

        Assert.NotEqual(response.RefreshToken, stored.TokenHash);
        Assert.Equal(_tokens.HashRefreshToken(response.RefreshToken), stored.TokenHash);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task AppleSignIn_KeepsTheFirstEmail_WhenAppleStopsSendingIt()
    {
        await SignInAsync();

        // Apple releases the email only on the first authorization; later tokens omit it.
        _verifier.Identity = new AppleIdentity(AppleSubject, null, false);
        await SignInAsync();

        Assert.Equal("alice@example.com", (await _db.Users.SingleAsync(Ct)).Email);
    }

    // ---------- BE-102: guest accounts ----------

    [Fact]
    public async Task Anonymous_CreatesGuestWithSettingsAndUsableTokens()
    {
        AuthTokensResponse response = await CreateGuestAsync();

        User user = await _db.Users.SingleAsync(Ct);

        Assert.True(user.IsAnonymous);
        Assert.Null(user.AppleUserId);
        Assert.True(response.User.IsAnonymous);
        Assert.True(response.User.IsNewUser);
        Assert.NotEmpty(response.AccessToken);
        Assert.True(await _db.UserSettings.AnyAsync(s => s.UserId == user.Id, Ct));
    }

    [Fact]
    public async Task LinkApple_PromotesGuestInPlace_WhenTheAppleIdIsUnused()
    {
        AuthTokensResponse guest = await CreateGuestAsync();
        _currentUser.UserId = guest.User.Id;

        LinkAppleResponse response = await LinkAsync();

        Assert.False(response.Merged);
        Assert.Null(response.AbsorbedUserId);

        // The whole point: same account id, so nothing the client already stored is invalidated.
        Assert.Equal(guest.User.Id, response.Tokens.User.Id);

        User user = await _db.Users.SingleAsync(Ct);
        Assert.Equal(AppleSubject, user.AppleUserId);
        Assert.False(user.IsAnonymous);
        Assert.Equal(1, await _db.Users.CountAsync(Ct));
    }

    [Fact]
    public async Task LinkApple_IsIdempotent_WhenTheCallerAlreadyOwnsThatAppleId()
    {
        AuthTokensResponse guest = await CreateGuestAsync();
        _currentUser.UserId = guest.User.Id;

        await LinkAsync();
        LinkAppleResponse replay = await LinkAsync();

        // A client retrying after a dropped response must not get an error.
        Assert.False(replay.Merged);
        Assert.Equal(guest.User.Id, replay.Tokens.User.Id);
    }

    [Fact]
    public async Task LinkApple_Conflicts_WhenTheCallerIsAlreadyLinkedToADifferentAppleId()
    {
        AuthTokensResponse guest = await CreateGuestAsync();
        _currentUser.UserId = guest.User.Id;
        await LinkAsync();

        _verifier.Identity = new AppleIdentity("999999.other.0000", null, false);

        // Re-pointing a registration would hand one person's history to another.
        await Assert.ThrowsAsync<AccountConflictException>(LinkAsync);
    }

    // ---------- BE-102 AC3: merge ----------

    [Fact]
    public async Task LinkApple_MergesGuestIntoTheRegisteredAccount_WhenTheAppleIdIsTaken()
    {
        AuthTokensResponse registered = await SignInAsync();

        AuthTokensResponse guest = await CreateGuestAsync();
        _currentUser.UserId = guest.User.Id;
        SeedGuestData(guest.User.Id, sessions: 2, checkins: 3);
        await _db.SaveChangesAsync(Ct);

        LinkAppleResponse response = await LinkAsync();

        Assert.True(response.Merged);
        Assert.Equal(guest.User.Id, response.AbsorbedUserId);

        // The registered account is preserved and is who the caller now is.
        Assert.Equal(registered.User.Id, response.Tokens.User.Id);
        Assert.Equal(new AccountMergeSummary(2, 3, 0), response.Migrated);

        Assert.Equal(2, await _db.UserSessions.CountAsync(s => s.UserId == registered.User.Id, Ct));
        Assert.Equal(3, await _db.EnergyCheckins.CountAsync(c => c.UserId == registered.User.Id, Ct));
        Assert.False(await _db.UserSessions.AnyAsync(s => s.UserId == guest.User.Id, Ct));

        User retired = await _db.Users.SingleAsync(u => u.Id == guest.User.Id, Ct);
        Assert.NotNull(retired.DeletedAt);
    }

    [Fact]
    public async Task LinkApple_BumpsMigratedRowsPastTheSurvivingDevicesCursor()
    {
        await SignInAsync();

        AuthTokensResponse guest = await CreateGuestAsync();
        _currentUser.UserId = guest.User.Id;
        SeedGuestData(guest.User.Id, sessions: 1, checkins: 1);
        await _db.SaveChangesAsync(Ct);

        _clock.Advance(TimeSpan.FromHours(1));
        await LinkAsync();

        // Migrated rows must land after the cursor the registered account's devices already hold,
        // or the next sync pull would never deliver them.
        Assert.All(
            await _db.UserSessions.ToListAsync(Ct),
            session => Assert.Equal(Noon.AddHours(1), session.UpdatedAt));
        Assert.All(
            await _db.EnergyCheckins.ToListAsync(Ct),
            checkin => Assert.Equal(Noon.AddHours(1), checkin.UpdatedAt));
    }

    [Fact]
    public async Task LinkApple_RevokesTheGuestRefreshTokens_OnMerge()
    {
        await SignInAsync();

        AuthTokensResponse guest = await CreateGuestAsync();
        _currentUser.UserId = guest.User.Id;

        await LinkAsync();

        string guestHash = _tokens.HashRefreshToken(guest.RefreshToken);
        RefreshToken retired = await _db.RefreshTokens.SingleAsync(t => t.TokenHash == guestHash, Ct);

        Assert.NotNull(retired.RevokedAt);
    }

    [Fact]
    public async Task LinkApple_RejectsAnonymousCallersWithNoSession()
    {
        _currentUser.UserId = null;

        await Assert.ThrowsAsync<AuthenticationFailedException>(LinkAsync);
    }

    // ---------- helpers ----------

    private Task<AuthTokensResponse> SignInAsync() =>
        new SignInWithAppleHandler(_db, _verifier, Issuer(), _clock)
            .Handle(new SignInWithAppleCommand(new AppleSignInRequest("token")), Ct);

    private Task<AuthTokensResponse> CreateGuestAsync() =>
        new CreateAnonymousUserHandler(_db, Issuer(), _clock)
            .Handle(new CreateAnonymousUserCommand(), Ct);

    private Task<LinkAppleResponse> LinkAsync() =>
        new LinkAppleAccountHandler(_db, _verifier, Issuer(), _currentUser, _clock)
            .Handle(new LinkAppleAccountCommand(new LinkAppleRequest("token")), Ct);

    private AuthTokenIssuer Issuer() => new(_db, _tokens);

    private void SeedGuestData(Guid userId, int sessions, int checkins)
    {
        for (int i = 0; i < sessions; i++)
        {
            _db.UserSessions.Add(new UserSession
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                SessionType = SessionType.Focus90,
                StartedAt = Noon,
                TargetDurationSeconds = 5_400,
                ClientCreatedAt = Noon,
                UpdatedAt = Noon,
            });
        }

        for (int i = 0; i < checkins; i++)
        {
            _db.EnergyCheckins.Add(new EnergyCheckin
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Score = 3,
                RecordedAt = Noon,
                UpdatedAt = Noon,
            });
        }
    }
}
