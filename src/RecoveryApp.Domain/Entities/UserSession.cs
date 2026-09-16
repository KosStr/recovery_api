using System.Text.Json;
using RecoveryApp.Domain.Common;
using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Domain.Entities;

/// <summary>
/// A focus, nap, NSDR or detox session recorded on device. The row is created offline, keyed by a
/// client generated UUIDv7, and reconciled by the sync endpoints.
/// </summary>
public sealed class UserSession : ISyncEntity
{
    /// <summary>Client generated (UUIDv7) primary key. Stable across devices.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Which protocol the user ran.</summary>
    public SessionType SessionType { get; set; }

    /// <summary>When the session started, as recorded on device.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the session ended. Null while a session is still in progress on device.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Duration the user committed to, in seconds.</summary>
    public int TargetDurationSeconds { get; set; }

    /// <summary>Duration actually completed, in seconds.</summary>
    public int ActualDurationSeconds { get; set; }

    /// <summary>Whether the session ran to its target without being abandoned.</summary>
    public bool CompletedSuccessfully { get; set; }

    /// <summary>How many times the session was interrupted (app backgrounded, call, unlock).</summary>
    public int InterruptionsCount { get; set; }

    /// <summary>Free-form client telemetry, persisted as a <c>jsonb</c> document.</summary>
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];

    /// <summary>Device clock timestamp of row creation. Never overwritten by the server.</summary>
    public DateTimeOffset ClientCreatedAt { get; set; }

    /// <summary>Revision stamp driving last-writer-wins merges and the sync cursor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Tombstone marker. Non-null rows are pulled by peers as deletions.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
