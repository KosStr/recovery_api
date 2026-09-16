using System.Text.Json.Serialization;

namespace RecoveryApp.Domain.Enums;

/// <summary>Content bucket an <see cref="Entities.AudioTrack"/> belongs to.</summary>
public enum AudioCategory
{
    /// <summary>Guided non-sleep deep rest narration.</summary>
    [JsonStringEnumMemberName("nsdr")]
    Nsdr = 1,

    /// <summary>Ambient soundscape loops.</summary>
    [JsonStringEnumMemberName("soundscape")]
    Soundscape = 2,

    /// <summary>Guided breathing exercises.</summary>
    [JsonStringEnumMemberName("breathing")]
    Breathing = 3,
}
