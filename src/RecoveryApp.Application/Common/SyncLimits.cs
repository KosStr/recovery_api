namespace RecoveryApp.Application.Common;

/// <summary>Protocol limits shared by the push and pull halves of the sync API.</summary>
public static class SyncLimits
{
    /// <summary>Maximum rows of a single entity type accepted in one push batch.</summary>
    public const int MaxPushBatchSize = 500;

    /// <summary>Default page size for a pull when the client does not ask for one.</summary>
    public const int DefaultPullPageSize = 250;

    /// <summary>Maximum page size a client may ask for on a pull.</summary>
    public const int MaxPullPageSize = 1000;

    /// <summary>
    /// Safety margin subtracted from the returned cursor. Guards against rows committed with an
    /// earlier timestamp than a concurrent transaction that already finished, which would otherwise
    /// be skipped by the next pull.
    /// </summary>
    public static readonly TimeSpan CursorOverlap = TimeSpan.FromSeconds(1);
}
