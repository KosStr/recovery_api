using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Entities;

namespace RecoveryApp.Application.Features.Sync;

/// <summary>Reads every row belonging to the caller that changed after a cursor, tombstones included.</summary>
/// <param name="Since">
/// The cursor returned by the previous pull. Pass <see cref="DateTimeOffset.MinValue"/> (or omit it
/// at the transport level) to bootstrap a device from scratch.
/// </param>
/// <param name="Limit">Maximum rows returned per entity type.</param>
public sealed record PullSyncChangesQuery(DateTimeOffset Since, int Limit) : IRequest<SyncPullResponse>;

/// <summary>Handles <see cref="PullSyncChangesQuery"/>.</summary>
/// <param name="db">Persistence surface.</param>
/// <param name="currentUser">Caller identity.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class PullSyncChangesHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<PullSyncChangesQuery, SyncPullResponse>
{
    /// <inheritdoc />
    public async Task<SyncPullResponse> Handle(PullSyncChangesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset since = query.Since;

        int limit = Math.Clamp(query.Limit, 1, SyncLimits.MaxPullPageSize);
        int probe = limit + 1;

        List<UserSessionDto> sessions = await db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.UpdatedAt > since)
            .OrderBy(s => s.UpdatedAt)
            .ThenBy(s => s.Id)
            .Take(probe)
            .Select(s => new UserSessionDto(
                s.Id,
                s.SessionType,
                s.StartedAt,
                s.EndedAt,
                s.TargetDurationSeconds,
                s.ActualDurationSeconds,
                s.CompletedSuccessfully,
                s.InterruptionsCount,
                s.Metadata,
                s.ClientCreatedAt,
                s.UpdatedAt,
                s.DeletedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<EnergyCheckinDto> checkins = await db.EnergyCheckins
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.UpdatedAt > since)
            .OrderBy(c => c.UpdatedAt)
            .ThenBy(c => c.Id)
            .Take(probe)
            .Select(c => new EnergyCheckinDto(
                c.Id,
                c.Score,
                c.Context,
                c.Tags,
                c.RecordedAt,
                c.UpdatedAt,
                c.DeletedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool moreSessions = sessions.Count > limit;
        bool moreCheckins = checkins.Count > limit;

        if (moreSessions)
        {
            sessions.RemoveAt(sessions.Count - 1);
        }

        if (moreCheckins)
        {
            checkins.RemoveAt(checkins.Count - 1);
        }

        UserSettings? settings = await db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.UpdatedAt > since, cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = moreSessions || moreCheckins;
        DateTimeOffset nextCursor = NextCursor(now, sessions, checkins, moreSessions, moreCheckins);

        return new SyncPullResponse(
            now,
            nextCursor,
            hasMore,
            sessions,
            checkins,
            settings is null ? null : ToResponse(settings));
    }

    /// <summary>
    /// When the page was truncated the cursor must not run past the oldest stream that still has
    /// rows waiting, otherwise the next pull would skip them. When everything was drained the cursor
    /// is the server clock, pulled back by a small overlap so that rows committed by transactions
    /// that were still open during this read are picked up next time.
    /// </summary>
    private static DateTimeOffset NextCursor(
        DateTimeOffset now,
        List<UserSessionDto> sessions,
        List<EnergyCheckinDto> checkins,
        bool moreSessions,
        bool moreCheckins)
    {
        if (!moreSessions && !moreCheckins)
        {
            return now - SyncLimits.CursorOverlap;
        }

        DateTimeOffset cursor = DateTimeOffset.MaxValue;

        if (moreSessions && sessions.Count > 0)
        {
            cursor = sessions[^1].UpdatedAt;
        }

        if (moreCheckins && checkins.Count > 0 && checkins[^1].UpdatedAt < cursor)
        {
            cursor = checkins[^1].UpdatedAt;
        }

        return cursor == DateTimeOffset.MaxValue ? now - SyncLimits.CursorOverlap : cursor;
    }

    private static UserSettingsResponse ToResponse(UserSettings settings) => new(
        settings.UserId,
        settings.TargetSleepTime,
        settings.TargetWakeTime,
        settings.CaffeineCutoffHours,
        settings.DigitalSunsetMinutes,
        settings.HapticEnabled,
        settings.SoundMixConfig,
        settings.UpdatedAt);
}
