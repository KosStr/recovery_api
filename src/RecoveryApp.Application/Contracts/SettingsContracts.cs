using RecoveryApp.Domain.Common;

namespace RecoveryApp.Application.Contracts;

/// <summary>Replaces the caller's sleep, caffeine and digital sunset configuration.</summary>
/// <param name="TargetSleepTime">Local wall-clock bedtime target.</param>
/// <param name="TargetWakeTime">Local wall-clock wake target.</param>
/// <param name="CaffeineCutoffHours">Hours before bedtime after which caffeine is discouraged (0-24).</param>
/// <param name="DigitalSunsetMinutes">Minutes before bedtime the digital sunset starts (0-480).</param>
/// <param name="HapticEnabled">Whether haptic cues play during sessions.</param>
/// <param name="SoundMixConfig">The persisted audio mix.</param>
public sealed record UpdateUserSettingsRequest(
    TimeOnly TargetSleepTime,
    TimeOnly TargetWakeTime,
    int CaffeineCutoffHours,
    int DigitalSunsetMinutes,
    bool HapticEnabled,
    SoundMixConfig SoundMixConfig);

/// <summary>The caller's current configuration.</summary>
/// <param name="UserId">Owning account.</param>
/// <param name="TargetSleepTime">Local wall-clock bedtime target.</param>
/// <param name="TargetWakeTime">Local wall-clock wake target.</param>
/// <param name="CaffeineCutoffHours">Hours before bedtime after which caffeine is discouraged.</param>
/// <param name="DigitalSunsetMinutes">Minutes before bedtime the digital sunset starts.</param>
/// <param name="HapticEnabled">Whether haptic cues play during sessions.</param>
/// <param name="SoundMixConfig">The persisted audio mix.</param>
/// <param name="UpdatedAt">Server revision stamp.</param>
public sealed record UserSettingsResponse(
    Guid UserId,
    TimeOnly TargetSleepTime,
    TimeOnly TargetWakeTime,
    int CaffeineCutoffHours,
    int DigitalSunsetMinutes,
    bool HapticEnabled,
    SoundMixConfig SoundMixConfig,
    DateTimeOffset UpdatedAt);
