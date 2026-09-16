using RecoveryApp.Domain.Common;

namespace RecoveryApp.Domain.Entities;

/// <summary>A one-tap subjective energy rating captured on device.</summary>
public sealed class EnergyCheckin : ISyncEntity
{
    /// <summary>Lowest accepted value for <see cref="Score"/>.</summary>
    public const int MinScore = 1;

    /// <summary>Highest accepted value for <see cref="Score"/>.</summary>
    public const int MaxScore = 5;

    /// <summary>Client generated (UUIDv7) primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Subjective energy from <see cref="MinScore"/> (depleted) to <see cref="MaxScore"/> (peak).</summary>
    public int Score { get; set; }

    /// <summary>Optional free text note about what the user was doing.</summary>
    public string? Context { get; set; }

    /// <summary>Structured labels, stored as a Postgres <c>text[]</c>.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Device clock timestamp the rating refers to.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Revision stamp driving last-writer-wins merges and the sync cursor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Tombstone marker. Non-null rows are pulled by peers as deletions.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
