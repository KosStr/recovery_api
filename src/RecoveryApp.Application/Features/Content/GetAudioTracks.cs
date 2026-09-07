using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Application.Features.Content;

/// <summary>Reads the audio catalogue, optionally filtered, with HTTP cache validation built in.</summary>
/// <param name="Category">Restrict to a single catalogue bucket, or null for everything.</param>
/// <param name="IncludePremium">Whether subscription-only tracks are returned.</param>
/// <param name="IfNoneMatch">The caller's cached validator, taken from the <c>If-None-Match</c> header.</param>
public sealed record GetAudioTracksQuery(
    AudioCategory? Category,
    bool IncludePremium,
    string? IfNoneMatch) : IRequest<AudioTrackCatalogResult>;

/// <summary>Outcome of <see cref="GetAudioTracksQuery"/>.</summary>
/// <param name="ETag">Strong validator for the requested slice of the catalogue.</param>
/// <param name="Catalog">
/// The catalogue page, or <see langword="null"/> when the caller's <c>If-None-Match</c> already
/// matches and the endpoint should answer <c>304 Not Modified</c>.
/// </param>
public sealed record AudioTrackCatalogResult(string ETag, AudioTrackCatalogResponse? Catalog)
{
    /// <summary>Whether the caller's cached copy is still current.</summary>
    public bool IsNotModified => Catalog is null;
}

/// <summary>Handles <see cref="GetAudioTracksQuery"/>.</summary>
/// <param name="db">Persistence surface.</param>
public sealed class GetAudioTracksHandler(IAppDbContext db)
    : IRequestHandler<GetAudioTracksQuery, AudioTrackCatalogResult>
{
    /// <inheritdoc />
    public async Task<AudioTrackCatalogResult> Handle(GetAudioTracksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Domain.Entities.AudioTrack> tracks = db.AudioTracks.AsNoTracking();

        if (query.Category is { } category)
        {
            tracks = tracks.Where(t => t.Category == category);
        }

        if (!query.IncludePremium)
        {
            tracks = tracks.Where(t => !t.IsPremium);
        }

        // One cheap aggregate settles cache validation before any row is materialised.
        var fingerprint = await tracks
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                MaxVersion = g.Max(t => t.Version),
                MaxId = g.Max(t => t.Id),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        string etag = BuildETag(
            query.Category,
            query.IncludePremium,
            fingerprint?.Count ?? 0,
            fingerprint?.MaxVersion ?? 0,
            fingerprint?.MaxId ?? Guid.Empty);

        if (Matches(query.IfNoneMatch, etag))
        {
            return new AudioTrackCatalogResult(etag, Catalog: null);
        }

        List<AudioTrackResponse> page = await tracks
            .OrderByDescending(t => t.Id)
            .Select(t => new AudioTrackResponse(
                t.Id,
                t.Title,
                t.Category,
                t.DurationSeconds,
                t.AudioUrl,
                t.FileSizeBytes,
                t.IsPremium,
                t.Version))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AudioTrackCatalogResult(etag, new AudioTrackCatalogResponse(page, etag));
    }

    private static string BuildETag(
        AudioCategory? category,
        bool includePremium,
        int count,
        int maxVersion,
        Guid maxId)
    {
        string seed = string.Create(
            CultureInfo.InvariantCulture,
            $"{category?.ToString() ?? "all"}|{includePremium}|{count}|{maxVersion}|{maxId}");

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return $"\"{Convert.ToHexStringLower(digest.AsSpan(0, 16))}\"";
    }

    private static bool Matches(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        foreach (string candidate in ifNoneMatch.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (candidate == "*" || string.Equals(candidate.TrimStart('W', '/'), etag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
