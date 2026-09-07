using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecoveryApp.Domain.Common;
using RecoveryApp.Domain.Entities;
using RecoveryApp.Infrastructure.Persistence.Converters;

namespace RecoveryApp.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> to <c>users</c>.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.AppleUserId).HasMaxLength(255).IsRequired();
        builder.Property(u => u.CreatedAt).HasColumnType("timestamptz");
        builder.Property(u => u.UpdatedAt).HasColumnType("timestamptz");
        builder.Property(u => u.DeletedAt).HasColumnType("timestamptz");

        builder.HasIndex(u => u.AppleUserId).IsUnique();

        // Partial unique index: recycled addresses on deleted accounts must not block a new signup.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("email IS NOT NULL AND deleted_at IS NULL");

        builder.HasOne(u => u.Settings)
            .WithOne(s => s.User)
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="UserSettings"/> to <c>user_settings</c>.</summary>
public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_settings");
        builder.HasKey(s => s.UserId);

        builder.Property(s => s.UserId).ValueGeneratedNever();
        builder.Property(s => s.TargetSleepTime).HasColumnType("time");
        builder.Property(s => s.TargetWakeTime).HasColumnType("time");
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz");

        builder.Property(s => s.SoundMixConfig)
            .HasColumnType("jsonb")
            .HasConversion(new JsonDocumentConverter<SoundMixConfig>(), new JsonDocumentComparer<SoundMixConfig>())
            .IsRequired();

        // Settings ride along with the sync pull, which filters on the revision stamp.
        builder.HasIndex(s => s.UpdatedAt);
    }
}
