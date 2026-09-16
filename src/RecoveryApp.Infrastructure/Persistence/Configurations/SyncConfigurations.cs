using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecoveryApp.Domain.Entities;
using RecoveryApp.Domain.Enums;
using RecoveryApp.Infrastructure.Persistence.Converters;

namespace RecoveryApp.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="UserSession"/> to <c>user_sessions</c>.</summary>
public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_sessions");
        builder.HasKey(s => s.Id);

        // The identifier is minted on device so the row survives an offline create.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.SessionType)
            .HasConversion(new EnumWireConverter<SessionType>())
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.StartedAt).HasColumnType("timestamptz");
        builder.Property(s => s.EndedAt).HasColumnType("timestamptz");
        builder.Property(s => s.ClientCreatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz");
        builder.Property(s => s.DeletedAt).HasColumnType("timestamptz");

        builder.Property(s => s.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                new JsonDocumentConverter<Dictionary<string, JsonElement>>(),
                new JsonDocumentComparer<Dictionary<string, JsonElement>>())
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The delta query for every pull: owner plus revision stamp, in that order.
        builder.HasIndex(s => new { s.UserId, s.UpdatedAt })
            .HasDatabaseName("ix_user_sessions_user_id_updated_at");

        builder.HasIndex(s => new { s.UserId, s.StartedAt })
            .HasDatabaseName("ix_user_sessions_user_id_started_at");
    }
}

/// <summary>Maps <see cref="EnergyCheckin"/> to <c>energy_checkins</c>.</summary>
public sealed class EnergyCheckinConfiguration : IEntityTypeConfiguration<EnergyCheckin>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EnergyCheckin> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("energy_checkins", table => table.HasCheckConstraint(
            "ck_energy_checkins_score",
            $"score BETWEEN {EnergyCheckin.MinScore} AND {EnergyCheckin.MaxScore}"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Context).HasMaxLength(1_000);
        builder.Property(c => c.Tags).HasColumnType("text[]").IsRequired();
        builder.Property(c => c.RecordedAt).HasColumnType("timestamptz");
        builder.Property(c => c.UpdatedAt).HasColumnType("timestamptz");
        builder.Property(c => c.DeletedAt).HasColumnType("timestamptz");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.UserId, c.UpdatedAt })
            .HasDatabaseName("ix_energy_checkins_user_id_updated_at");

        builder.HasIndex(c => new { c.UserId, c.RecordedAt })
            .HasDatabaseName("ix_energy_checkins_user_id_recorded_at");
    }
}

/// <summary>Maps <see cref="SyncChangelog"/> to <c>sync_changelog</c>.</summary>
public sealed class SyncChangelogConfiguration : IEntityTypeConfiguration<SyncChangelog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SyncChangelog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sync_changelog");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).UseIdentityAlwaysColumn();
        builder.Property(c => c.EntityName).HasMaxLength(64).IsRequired();
        builder.Property(c => c.ChangedAt).HasColumnType("timestamptz");

        builder.Property(c => c.Operation)
            .HasConversion(new EnumWireConverter<SyncOperation>())
            .HasMaxLength(16)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.UserId, c.ChangedAt })
            .HasDatabaseName("ix_sync_changelog_user_id_changed_at");
    }
}
