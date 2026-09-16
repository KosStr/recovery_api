using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Infrastructure.Persistence;

namespace RecoveryApp.UnitTests;

/// <summary>
/// Builds a throwaway <see cref="AppDbContext"/> on the in-memory provider so the auth handlers can
/// be exercised end to end without Postgres.
/// </summary>
/// <remarks>
/// The in-memory provider does not enforce the relational parts of the model — check constraints,
/// partial indexes, jsonb column types — so these are behaviour tests for the handlers, not schema
/// tests. The schema is covered by the migration.
/// </remarks>
internal static class TestDb
{
    public static AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"recoveryapp-{Guid.CreateVersion7()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}

/// <summary>A clock the tests drive by hand.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Returns whichever identity the test hands it, without touching the network.</summary>
internal sealed class StubVerifier(AppleIdentity identity) : IAppleIdentityTokenVerifier
{
    public AppleIdentity Identity { get; set; } = identity;

    public Task<AppleIdentity> VerifyAsync(string identityToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(Identity);
}

/// <summary>A caller identity the test controls.</summary>
internal sealed class TestCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; set; } = userId;

    public Guid RequireUserId() =>
        UserId ?? throw new AuthenticationFailedException("No authenticated user.");
}
