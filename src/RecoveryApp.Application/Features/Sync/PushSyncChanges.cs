using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RecoveryApp.Application.Abstractions;
using RecoveryApp.Application.Common;
using RecoveryApp.Application.Contracts;
using RecoveryApp.Domain.Common;
using RecoveryApp.Domain.Entities;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Application.Features.Sync;

/// <summary>Reconciles a batch of offline changes into the server tables using last-writer-wins.</summary>
/// <param name="Request">The pushed batch.</param>
public sealed record PushSyncChangesCommand(SyncPushRequest Request) : IRequest<SyncPushResponse>;

/// <summary>Validates <see cref="SyncPushRequest"/>.</summary>
public sealed class SyncPushRequestValidator : AbstractValidator<SyncPushRequest>
{
    /// <summary>Initializes a new instance of the <see cref="SyncPushRequestValidator"/> class.</summary>
    public SyncPushRequestValidator()
    {
        RuleFor(x => x.Sessions)
            .Must(s => s is null || s.Count <= SyncLimits.MaxPushBatchSize)
            .WithMessage($"A push may not carry more than {SyncLimits.MaxPushBatchSize} sessions.");

        RuleFor(x => x.EnergyCheckins)
            .Must(c => c is null || c.Count <= SyncLimits.MaxPushBatchSize)
            .WithMessage($"A push may not carry more than {SyncLimits.MaxPushBatchSize} energy check-ins.");

        RuleForEach(x => x.Sessions).ChildRules(session =>
        {
            session.RuleFor(s => s.Id).NotEmpty();
            session.RuleFor(s => s.SessionType).IsInEnum();
            session.RuleFor(s => s.TargetDurationSeconds).InclusiveBetween(0, 86_400);
            session.RuleFor(s => s.ActualDurationSeconds).InclusiveBetween(0, 86_400);
            session.RuleFor(s => s.InterruptionsCount).GreaterThanOrEqualTo(0);
            session.RuleFor(s => s.UpdatedAt).NotEqual(default(DateTimeOffset));
            session.RuleFor(s => s.EndedAt)
                .GreaterThanOrEqualTo(s => s.StartedAt)
                .When(s => s.EndedAt.HasValue);
        });

        RuleForEach(x => x.EnergyCheckins).ChildRules(checkin =>
        {
            checkin.RuleFor(c => c.Id).NotEmpty();
            checkin.RuleFor(c => c.Score).InclusiveBetween(EnergyCheckin.MinScore, EnergyCheckin.MaxScore);
            checkin.RuleFor(c => c.Context).MaximumLength(1_000);
            checkin.RuleFor(c => c.Tags)
                .Must(t => t is null || t.Count <= 32)
                .WithMessage("A check-in may not carry more than 32 tags.");
            checkin.RuleFor(c => c.UpdatedAt).NotEqual(default(DateTimeOffset));
        });
    }
}

/// <summary>Handles <see cref="PushSyncChangesCommand"/>.</summary>
/// <param name="db">Persistence surface.</param>
/// <param name="currentUser">Caller identity.</param>
/// <param name="timeProvider">Server clock.</param>
public sealed class PushSyncChangesHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<PushSyncChangesCommand, SyncPushResponse>
{
    /// <inheritdoc />
    public async Task<SyncPushResponse> Handle(PushSyncChangesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid userId = currentUser.RequireUserId();
        DateTimeOffset now = timeProvider.GetUtcNow();

        IReadOnlyList<UserSessionDto> sessions = command.Request.Sessions ?? [];
        IReadOnlyList<EnergyCheckinDto> checkins = command.Request.EnergyCheckins ?? [];

        List<SyncConflict> conflicts = [];

        SyncEntityResult sessionResult = await MergeSessionsAsync(userId, now, sessions, conflicts, cancellationToken)
            .ConfigureAwait(false);

        SyncEntityResult checkinResult = await MergeCheckinsAsync(userId, now, checkins, conflicts, cancellationToken)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SyncPushResponse(now, sessionResult, checkinResult, conflicts);
    }

    private async Task<SyncEntityResult> MergeSessionsAsync(
        Guid userId,
        DateTimeOffset now,
        IReadOnlyList<UserSessionDto> incoming,
        List<SyncConflict> conflicts,
        CancellationToken cancellationToken)
    {
        if (incoming.Count == 0)
        {
            return new SyncEntityResult(0, 0, 0);
        }

        // A retried batch can carry the same id twice; keep only the newest revision per id.
        Dictionary<Guid, UserSessionDto> deduplicated = Deduplicate(incoming, dto => dto.Id, dto => dto.UpdatedAt);
        Guid[] ids = [.. deduplicated.Keys];

        Dictionary<Guid, UserSession> stored = await db.UserSessions
            .Where(s => s.UserId == userId && ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken)
            .ConfigureAwait(false);

        int inserted = 0;
        int updated = 0;
        int skipped = 0;

        foreach (UserSessionDto dto in deduplicated.Values)
        {
            SyncOperation operation = dto.DeletedAt is null ? SyncOperation.Update : SyncOperation.Delete;

            if (stored.TryGetValue(dto.Id, out UserSession? row))
            {
                if (!SyncMergePolicy.ShouldApply(dto.UpdatedAt, row.UpdatedAt))
                {
                    skipped++;
                    conflicts.Add(new SyncConflict(nameof(UserSession), dto.Id, row.UpdatedAt, dto.UpdatedAt));
                    continue;
                }

                Apply(dto, row);
                updated++;
            }
            else
            {
                row = new UserSession { Id = dto.Id, UserId = userId };
                Apply(dto, row);
                db.UserSessions.Add(row);
                inserted++;
                operation = dto.DeletedAt is null ? SyncOperation.Insert : SyncOperation.Delete;
            }

            RecordChange(userId, nameof(UserSession), dto.Id, operation, now);
        }

        return new SyncEntityResult(inserted, updated, skipped);
    }

    private async Task<SyncEntityResult> MergeCheckinsAsync(
        Guid userId,
        DateTimeOffset now,
        IReadOnlyList<EnergyCheckinDto> incoming,
        List<SyncConflict> conflicts,
        CancellationToken cancellationToken)
    {
        if (incoming.Count == 0)
        {
            return new SyncEntityResult(0, 0, 0);
        }

        Dictionary<Guid, EnergyCheckinDto> deduplicated = Deduplicate(incoming, dto => dto.Id, dto => dto.UpdatedAt);
        Guid[] ids = [.. deduplicated.Keys];

        Dictionary<Guid, EnergyCheckin> stored = await db.EnergyCheckins
            .Where(c => c.UserId == userId && ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        int inserted = 0;
        int updated = 0;
        int skipped = 0;

        foreach (EnergyCheckinDto dto in deduplicated.Values)
        {
            SyncOperation operation = dto.DeletedAt is null ? SyncOperation.Update : SyncOperation.Delete;

            if (stored.TryGetValue(dto.Id, out EnergyCheckin? row))
            {
                if (!SyncMergePolicy.ShouldApply(dto.UpdatedAt, row.UpdatedAt))
                {
                    skipped++;
                    conflicts.Add(new SyncConflict(nameof(EnergyCheckin), dto.Id, row.UpdatedAt, dto.UpdatedAt));
                    continue;
                }

                Apply(dto, row);
                updated++;
            }
            else
            {
                row = new EnergyCheckin { Id = dto.Id, UserId = userId };
                Apply(dto, row);
                db.EnergyCheckins.Add(row);
                inserted++;
                operation = dto.DeletedAt is null ? SyncOperation.Insert : SyncOperation.Delete;
            }

            RecordChange(userId, nameof(EnergyCheckin), dto.Id, operation, now);
        }

        return new SyncEntityResult(inserted, updated, skipped);
    }

    private static Dictionary<Guid, T> Deduplicate<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> keySelector,
        Func<T, DateTimeOffset> revisionSelector)
    {
        Dictionary<Guid, T> map = new(items.Count);

        foreach (T item in items)
        {
            Guid key = keySelector(item);

            if (!map.TryGetValue(key, out T? existing)
                || SyncMergePolicy.ShouldApply(revisionSelector(item), revisionSelector(existing)))
            {
                map[key] = item;
            }
        }

        return map;
    }

    private static void Apply(UserSessionDto dto, UserSession row)
    {
        row.SessionType = dto.SessionType;
        row.StartedAt = dto.StartedAt;
        row.EndedAt = dto.EndedAt;
        row.TargetDurationSeconds = dto.TargetDurationSeconds;
        row.ActualDurationSeconds = dto.ActualDurationSeconds;
        row.CompletedSuccessfully = dto.CompletedSuccessfully;
        row.InterruptionsCount = dto.InterruptionsCount;
        row.Metadata = dto.Metadata is null ? [] : new Dictionary<string, JsonElement>(dto.Metadata);
        row.ClientCreatedAt = dto.ClientCreatedAt;
        row.UpdatedAt = dto.UpdatedAt;
        row.DeletedAt = dto.DeletedAt;
    }

    private static void Apply(EnergyCheckinDto dto, EnergyCheckin row)
    {
        row.Score = dto.Score;
        row.Context = dto.Context;
        row.Tags = dto.Tags is null ? [] : [.. dto.Tags];
        row.RecordedAt = dto.RecordedAt;
        row.UpdatedAt = dto.UpdatedAt;
        row.DeletedAt = dto.DeletedAt;
    }

    private void RecordChange(Guid userId, string entityName, Guid entityId, SyncOperation operation, DateTimeOffset now) =>
        db.SyncChangelogs.Add(new SyncChangelog
        {
            UserId = userId,
            EntityName = entityName,
            EntityId = entityId,
            Operation = operation,
            ChangedAt = now,
        });
}
