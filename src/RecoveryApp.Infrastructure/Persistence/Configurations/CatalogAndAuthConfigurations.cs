using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecoveryApp.Domain.Entities;
using RecoveryApp.Domain.Enums;
using RecoveryApp.Infrastructure.Persistence.Converters;

namespace RecoveryApp.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AudioTrack"/> to <c>audio_tracks</c>.</summary>
public sealed class AudioTrackConfiguration : IEntityTypeConfiguration<AudioTrack>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AudioTrack> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audio_tracks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.AudioUrl).HasMaxLength(2_048).IsRequired();
        builder.Property(t => t.Version).HasDefaultValue(1);

        builder.Property(t => t.Category)
            .HasConversion(new EnumWireConverter<AudioCategory>())
            .HasMaxLength(32)
            .IsRequired();

        // Serves the catalogue listing and its ETag aggregate without a sort.
        builder.HasIndex(t => new { t.Category, t.IsPremium })
            .HasDatabaseName("ix_audio_tracks_category_is_premium");
    }
}

/// <summary>Maps <see cref="RefreshToken"/> to <c>refresh_tokens</c>.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz");
        builder.Property(t => t.ExpiresAt).HasColumnType("timestamptz");
        builder.Property(t => t.RevokedAt).HasColumnType("timestamptz");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });
    }
}

/// <summary>Maps <see cref="IdempotencyRecord"/> to <c>idempotency_records</c>.</summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("idempotency_records");

        // Scoping the key by user makes collisions across accounts impossible.
        builder.HasKey(r => new { r.UserId, r.Key });

        builder.Property(r => r.Key).HasMaxLength(128);
        builder.Property(r => r.Endpoint).HasMaxLength(256).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.ResponseBody).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnType("timestamptz");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sweep target for the retention job that prunes expired keys.
        builder.HasIndex(r => r.CreatedAt);
    }
}
