namespace RecoveryApp.Domain.Common;

/// <summary>
/// A row that participates in the local-first two-way sync protocol: it is owned by a single user,
/// created on device with a client generated identifier, and tombstoned rather than hard deleted so
/// that other devices can converge.
/// </summary>
public interface ISyncEntity
{
    /// <summary>The client generated (UUIDv7) primary key.</summary>
    Guid Id { get; }

    /// <summary>Owner of the row. Every sync query is scoped by this value.</summary>
    Guid UserId { get; }

    /// <summary>Server-authoritative revision stamp used as the sync cursor and last-writer-wins clock.</summary>
    DateTimeOffset UpdatedAt { get; }

    /// <summary>Tombstone marker. Non-null rows are deletions that still have to be pulled by peers.</summary>
    DateTimeOffset? DeletedAt { get; }
}
