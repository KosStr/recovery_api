using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using RecoveryApp.Domain.Entities;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds the audio catalogue. Intended for local development and
/// integration tests; production deployments should run <c>dotnet ef database update</c> (or a
/// generated SQL script) as a separate, gated release step.
/// </summary>
/// <param name="db">The context to migrate.</param>
/// <param name="timeProvider">Server clock.</param>
/// <param name="logger">Logger.</param>
public sealed class DatabaseInitializer(AppDbContext db, TimeProvider timeProvider, ILogger<DatabaseInitializer> logger)
{
    /// <summary>
    /// Applies pending migrations and seeds the catalogue, logging and swallowing connection
    /// failures so a developer can still browse the API surface before <c>docker compose up</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the database was reachable and is now current.</returns>
    public async Task<bool> TryMigrateAndSeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await MigrateAsync(cancellationToken).ConfigureAwait(false);
            await SeedAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or DbException or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "Could not reach Postgres at startup. The API is running but every data endpoint will fail "
                + "until the database is available. Start it with 'docker compose up -d postgres'.");

            return false;
        }
    }

    /// <summary>Applies pending migrations.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the schema is current.</returns>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        IEnumerable<string> pending = await db.Database.GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);

        string[] names = [.. pending];

        if (names.Length == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}.", names.Length, string.Join(", ", names));
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserts the starter audio catalogue if the table is empty.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when seeding is done.</returns>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.AudioTracks.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        db.AudioTracks.AddRange(
            NewTrack(now, "Yoga Nidra: Full Body Scan", AudioCategory.Nsdr, 1_200, 18_400_000, isPremium: false),
            NewTrack(now, "NSDR: 20 Minute Reset", AudioCategory.Nsdr, 1_200, 18_200_000, isPremium: false),
            NewTrack(now, "Deep Rest for Sleep Debt", AudioCategory.Nsdr, 1_800, 27_500_000, isPremium: true),
            NewTrack(now, "Rain on a Tin Roof", AudioCategory.Soundscape, 3_600, 52_000_000, isPremium: false),
            NewTrack(now, "Coastal Night", AudioCategory.Soundscape, 3_600, 51_300_000, isPremium: true),
            NewTrack(now, "Box Breathing 4-4-4-4", AudioCategory.Breathing, 300, 4_600_000, isPremium: false),
            NewTrack(now, "Physiological Sigh", AudioCategory.Breathing, 180, 2_800_000, isPremium: false),
            NewTrack(now, "4-7-8 Wind Down", AudioCategory.Breathing, 420, 6_400_000, isPremium: true));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Seeded the starter audio catalogue.");
    }

    private static AudioTrack NewTrack(
        DateTimeOffset now,
        string title,
        AudioCategory category,
        int durationSeconds,
        long fileSizeBytes,
        bool isPremium)
    {
        string slug = title.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace(":", string.Empty, StringComparison.Ordinal);

        return new AudioTrack
        {
            Id = Guid.CreateVersion7(now),
            Title = title,
            Category = category,
            DurationSeconds = durationSeconds,
            AudioUrl = $"https://cdn.recoveryapp.local/audio/{slug}.m4a",
            FileSizeBytes = fileSizeBytes,
            IsPremium = isPremium,
            Version = 1,
        };
    }
}
