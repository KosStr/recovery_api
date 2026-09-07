using RecoveryApp.Domain.Enums;

namespace RecoveryApp.Domain.Entities;

/// <summary>
/// Append-only audit of every mutation the sync endpoints applied. Gives support and analytics a
/// replayable history that the last-writer-wins tables themselves cannot provide.
/// </summary>
public sealed class SyncChangelog
{
    /// <summary>Database generated, strictly increasing sequence number.</summary>
    public long Id { get; set; }

    /// <summary>Owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary>CLR name of the mutated entity, for example <c>UserSession</c>.</summary>
    public required string EntityName { get; set; }

    /// <summary>Primary key of the mutated row.</summary>
    public Guid EntityId { get; set; }

    /// <summary>What happened to the row.</summary>
    public SyncOperation Operation { get; set; }

    /// <summary>Server clock timestamp of the mutation.</summary>
    public DateTimeOffset ChangedAt { get; set; }
}
