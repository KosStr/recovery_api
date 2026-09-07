namespace RecoveryApp.Domain.Common;

/// <summary>
/// The user's persisted audio mix, stored as a single <c>jsonb</c> column on
/// <see cref="Entities.UserSettings"/> so that new layers can ship without a migration.
/// </summary>
public sealed record SoundMixConfig
{
    /// <summary>Master output level, 0.0 to 1.0.</summary>
    public double MasterVolume { get; init; } = 1.0;

    /// <summary>Individual layers that make up the mix.</summary>
    public IReadOnlyList<SoundLayer> Layers { get; init; } = [];

    /// <summary>Fade-out applied when a session ends, in seconds.</summary>
    public int FadeOutSeconds { get; init; } = 10;
}

/// <summary>A single addressable layer inside a <see cref="SoundMixConfig"/>.</summary>
public sealed record SoundLayer
{
    /// <summary>Stable identifier of the sound asset, for example <c>rain_heavy</c>.</summary>
    public required string SoundId { get; init; }

    /// <summary>Layer level, 0.0 to 1.0.</summary>
    public double Volume { get; init; } = 0.5;

    /// <summary>Whether the layer is currently audible.</summary>
    public bool Enabled { get; init; } = true;
}
