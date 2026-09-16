using System.Text.Json.Serialization;

namespace RecoveryApp.Domain.Enums;

/// <summary>The kind of recovery session a user ran on device.</summary>
public enum SessionType
{
    /// <summary>A 90 minute deep-focus block.</summary>
    [JsonStringEnumMemberName("focus_90")]
    Focus90 = 1,

    /// <summary>A short restorative nap (typically 20 minutes).</summary>
    [JsonStringEnumMemberName("power_nap")]
    PowerNap = 2,

    /// <summary>Non-sleep deep rest.</summary>
    [JsonStringEnumMemberName("nsdr")]
    Nsdr = 3,

    /// <summary>A screen-free digital detox window.</summary>
    [JsonStringEnumMemberName("digital_detox")]
    DigitalDetox = 4,
}
