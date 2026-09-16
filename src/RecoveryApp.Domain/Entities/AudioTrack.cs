using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Domain.Entities;

/// <summary>A downloadable audio asset served from the content catalogue. Global, not per user.</summary>
public sealed class AudioTrack
{
    /// <summary>Time ordered (UUIDv7) primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Display title shown in the catalogue.</summary>
    public required string Title { get; set; }

    /// <summary>Catalogue bucket the track belongs to.</summary>
    public AudioCategory Category { get; set; }

    /// <summary>Playback length in seconds.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Absolute URL (CDN) the client streams or downloads from.</summary>
    public required string AudioUrl { get; set; }

    /// <summary>Size of the asset in bytes, so the client can budget offline downloads.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Whether the track requires an active subscription.</summary>
    public bool IsPremium { get; set; }

    /// <summary>
    /// Monotonic asset revision. Bumped whenever the audio file behind <see cref="AudioUrl"/>
    /// changes so that clients re-download and the catalogue ETag rotates.
    /// </summary>
    public int Version { get; set; } = 1;
}
