using RecoveryApp.Domain.Common;

namespace RecoveryApp.Domain.Entities;

/// <summary>Per-user sleep, caffeine and digital sunset configuration. One row per user.</summary>
public sealed class UserSettings
{
    /// <summary>Owning user. Also the primary key: settings are a one-to-one extension of the account.</summary>
    public Guid UserId { get; set; }

    /// <summary>Local wall-clock bedtime target, for example <c>22:30</c>.</summary>
    public TimeOnly TargetSleepTime { get; set; } = new(22, 30);

    /// <summary>Local wall-clock wake target, for example <c>06:30</c>.</summary>
    public TimeOnly TargetWakeTime { get; set; } = new(6, 30);

    /// <summary>Hours before <see cref="TargetSleepTime"/> after which caffeine is discouraged.</summary>
    public int CaffeineCutoffHours { get; set; } = 8;

    /// <summary>Minutes before <see cref="TargetSleepTime"/> at which the digital sunset begins.</summary>
    public int DigitalSunsetMinutes { get; set; } = 60;

    /// <summary>Whether haptic cues are played during breathing and wind-down sessions.</summary>
    public bool HapticEnabled { get; set; } = true;

    /// <summary>The user's audio mix, persisted as a <c>jsonb</c> document.</summary>
    public SoundMixConfig SoundMixConfig { get; set; } = new();

    /// <summary>Server-authoritative revision stamp, used as the settings sync cursor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Navigation back to the owning account.</summary>
    public User? User { get; set; }
}
