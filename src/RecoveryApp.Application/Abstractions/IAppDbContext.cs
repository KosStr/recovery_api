using Microsoft.EntityFrameworkCore;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Abstractions;

/// <summary>
/// The persistence surface the application layer is allowed to touch. Keeps handlers free of a
/// direct reference to the EF Core context so they stay unit testable and Infrastructure stays
/// replaceable.
/// </summary>
public interface IAppDbContext
{
    /// <summary>Accounts.</summary>
    DbSet<User> Users { get; }

    /// <summary>One settings row per account.</summary>
    DbSet<UserSettings> UserSettings { get; }

    /// <summary>The global audio catalogue.</summary>
    DbSet<AudioTrack> AudioTracks { get; }

    /// <summary>Synced recovery sessions.</summary>
    DbSet<UserSession> UserSessions { get; }

    /// <summary>Synced energy ratings.</summary>
    DbSet<EnergyCheckin> EnergyCheckins { get; }

    /// <summary>Append-only sync audit trail.</summary>
    DbSet<SyncChangelog> SyncChangelogs { get; }

    /// <summary>Issued refresh tokens (hashed).</summary>
    DbSet<RefreshToken> RefreshTokens { get; }

    /// <summary>Cached responses for replayed <c>Idempotency-Key</c> requests.</summary>
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    /// <summary>Commits the unit of work.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
