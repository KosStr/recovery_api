using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Application.Contracts;

/// <summary>A single catalogue entry.</summary>
/// <param name="Id">Track id.</param>
/// <param name="Title">Display title.</param>
/// <param name="Category">Catalogue bucket.</param>
/// <param name="DurationSeconds">Playback length in seconds.</param>
/// <param name="AudioUrl">Absolute CDN URL for streaming or download.</param>
/// <param name="FileSizeBytes">Asset size in bytes, for offline download budgeting.</param>
/// <param name="IsPremium">Whether the track requires an active subscription.</param>
/// <param name="Version">Asset revision; a change means the client must re-download.</param>
public sealed record AudioTrackResponse(
    Guid Id,
    string Title,
    AudioCategory Category,
    int DurationSeconds,
    string AudioUrl,
    long FileSizeBytes,
    bool IsPremium,
    int Version);

/// <summary>The catalogue page returned by the content endpoint.</summary>
/// <param name="Tracks">Matching tracks, newest first.</param>
/// <param name="ETag">Strong validator for the whole result; send it back as <c>If-None-Match</c>.</param>
public sealed record AudioTrackCatalogResponse(IReadOnlyList<AudioTrackResponse> Tracks, string ETag);
