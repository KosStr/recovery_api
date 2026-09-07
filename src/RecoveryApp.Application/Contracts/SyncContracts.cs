using System.Text.Json;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Application.Contracts;

/// <summary>
/// A recovery session as it travels in both directions over the sync protocol. The client owns
/// <see cref="Id"/> and <see cref="UpdatedAt"/>; the server never rewrites them.
/// </summary>
/// <param name="Id">Client generated UUIDv7 primary key.</param>
/// <param name="SessionType">Which protocol the user ran.</param>
/// <param name="StartedAt">Device clock start time.</param>
/// <param name="EndedAt">Device clock end time, null while still running.</param>
/// <param name="TargetDurationSeconds">Duration the user committed to.</param>
/// <param name="ActualDurationSeconds">Duration actually completed.</param>
/// <param name="CompletedSuccessfully">Whether the session ran to target.</param>
/// <param name="InterruptionsCount">How many times the session was interrupted.</param>
/// <param name="Metadata">Free-form client telemetry.</param>
/// <param name="ClientCreatedAt">Device clock creation time; never overwritten.</param>
/// <param name="UpdatedAt">Client revision stamp driving last-writer-wins.</param>
/// <param name="DeletedAt">Tombstone marker; non-null means the row was deleted on device.</param>
public sealed record UserSessionDto(
    Guid Id,
    SessionType SessionType,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TargetDurationSeconds,
    int ActualDurationSeconds,
    bool CompletedSuccessfully,
    int InterruptionsCount,
    Dictionary<string, JsonElement> Metadata,
    DateTimeOffset ClientCreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

/// <summary>An energy rating as it travels in both directions over the sync protocol.</summary>
/// <param name="Id">Client generated UUIDv7 primary key.</param>
/// <param name="Score">Subjective energy from 1 (depleted) to 5 (peak).</param>
/// <param name="Context">Optional free text note.</param>
/// <param name="Tags">Structured labels.</param>
/// <param name="RecordedAt">Device clock timestamp the rating refers to.</param>
/// <param name="UpdatedAt">Client revision stamp driving last-writer-wins.</param>
/// <param name="DeletedAt">Tombstone marker; non-null means the row was deleted on device.</param>
public sealed record EnergyCheckinDto(
    Guid Id,
    int Score,
    string? Context,
    IReadOnlyList<string> Tags,
    DateTimeOffset RecordedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

/// <summary>A batch of local changes to reconcile with the server.</summary>
/// <param name="Sessions">Sessions created, updated or tombstoned on device.</param>
/// <param name="EnergyCheckins">Energy ratings created, updated or tombstoned on device.</param>
public sealed record SyncPushRequest(
    IReadOnlyList<UserSessionDto>? Sessions,
    IReadOnlyList<EnergyCheckinDto>? EnergyCheckins);

/// <summary>Per-entity outcome of a push batch.</summary>
/// <param name="Inserted">Rows that did not exist on the server and were created.</param>
/// <param name="Updated">Rows overwritten because the client revision was newer.</param>
/// <param name="Skipped">Rows ignored because the server already held an equal or newer revision.</param>
public sealed record SyncEntityResult(int Inserted, int Updated, int Skipped);

/// <summary>A row the server refused to overwrite, with the revision it kept.</summary>
/// <param name="EntityName">CLR name of the entity, for example <c>UserSession</c>.</param>
/// <param name="EntityId">Primary key of the row.</param>
/// <param name="ServerUpdatedAt">The revision stamp the server kept.</param>
/// <param name="ClientUpdatedAt">The revision stamp that lost.</param>
public sealed record SyncConflict(
    string EntityName,
    Guid EntityId,
    DateTimeOffset ServerUpdatedAt,
    DateTimeOffset ClientUpdatedAt);

/// <summary>The result of reconciling a push batch.</summary>
/// <param name="ServerTimestamp">Server clock at commit time. Use it as the next pull cursor.</param>
/// <param name="Sessions">Outcome for the session batch.</param>
/// <param name="EnergyCheckins">Outcome for the energy rating batch.</param>
/// <param name="Conflicts">Rows the server kept because its revision was newer.</param>
public sealed record SyncPushResponse(
    DateTimeOffset ServerTimestamp,
    SyncEntityResult Sessions,
    SyncEntityResult EnergyCheckins,
    IReadOnlyList<SyncConflict> Conflicts);

/// <summary>Everything that changed for the caller since a cursor, including tombstones.</summary>
/// <param name="ServerTimestamp">Server clock at read time.</param>
/// <param name="NextCursor">Cursor to pass as <c>since</c> on the next pull.</param>
/// <param name="HasMore">Whether more rows are waiting beyond this page.</param>
/// <param name="Sessions">Changed sessions, oldest revision first.</param>
/// <param name="EnergyCheckins">Changed energy ratings, oldest revision first.</param>
/// <param name="Settings">The settings row when it changed after the cursor, otherwise null.</param>
public sealed record SyncPullResponse(
    DateTimeOffset ServerTimestamp,
    DateTimeOffset NextCursor,
    bool HasMore,
    IReadOnlyList<UserSessionDto> Sessions,
    IReadOnlyList<EnergyCheckinDto> EnergyCheckins,
    UserSettingsResponse? Settings);
