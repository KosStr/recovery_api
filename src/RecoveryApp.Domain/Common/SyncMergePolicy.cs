namespace RecoveryApp.Domain.Common;

/// <summary>
/// Conflict resolution for the push half of the sync protocol.
/// </summary>
/// <remarks>
/// The protocol is last-writer-wins on the client supplied revision stamp. Ties are resolved in
/// favour of the row already on the server, which makes a replayed batch a no-op and therefore makes
/// <c>POST /api/v1/sync/push</c> idempotent even without an <c>Idempotency-Key</c> header. Deletes
/// are treated as ordinary revisions, so a tombstone only wins if it is strictly newer.
/// </remarks>
public static class SyncMergePolicy
{
    /// <summary>
    /// Decides whether an incoming client revision should overwrite the stored row.
    /// </summary>
    /// <param name="incomingUpdatedAt">Revision stamp carried by the pushed row.</param>
    /// <param name="storedUpdatedAt">Revision stamp of the row currently on the server.</param>
    /// <returns><see langword="true"/> when the incoming revision is strictly newer.</returns>
    public static bool ShouldApply(DateTimeOffset incomingUpdatedAt, DateTimeOffset storedUpdatedAt) =>
        incomingUpdatedAt > storedUpdatedAt;
}
