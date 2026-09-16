using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the service. Table and column names are produced by the
/// snake_case naming convention configured in
/// <see cref="DependencyInjection.AddInfrastructure"/>, so entity properties stay idiomatic C#
/// while the schema stays idiomatic Postgres.
/// </summary>
/// <param name="options">Context options supplied by the container.</param>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    /// <inheritdoc />
    public DbSet<User> Users => Set<User>();

    /// <inheritdoc />
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    /// <inheritdoc />
    public DbSet<AudioTrack> AudioTracks => Set<AudioTrack>();

    /// <inheritdoc />
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    /// <inheritdoc />
    public DbSet<EnergyCheckin> EnergyCheckins => Set<EnergyCheckin>();

    /// <inheritdoc />
    public DbSet<SyncChangelog> SyncChangelogs => Set<SyncChangelog>();

    /// <inheritdoc />
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc />
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(modelBuilder);
    }
}
