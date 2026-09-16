using RecoveryApp.Domain.Common;

namespace RecoveryApp.UnitTests;

/// <summary>
/// The merge policy is the whole correctness story of the sync protocol, so it is pinned here
/// rather than only exercised indirectly through the push handler.
/// </summary>
public sealed class SyncMergePolicyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewerClientRevisionWins()
    {
        Assert.True(SyncMergePolicy.ShouldApply(Noon.AddSeconds(1), Noon));
    }

    [Fact]
    public void OlderClientRevisionLoses()
    {
        Assert.False(SyncMergePolicy.ShouldApply(Noon.AddSeconds(-1), Noon));
    }

    [Fact]
    public void EqualRevisionIsSkipped_SoAReplayedBatchIsANoOp()
    {
        Assert.False(SyncMergePolicy.ShouldApply(Noon, Noon));
    }

    [Fact]
    public void ComparisonIsAbsolute_NotWallClock()
    {
        // Same instant, different offsets: a device in Berlin must not beat one in UTC.
        DateTimeOffset berlin = new(2026, 9, 7, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.False(SyncMergePolicy.ShouldApply(berlin, Noon));
        Assert.False(SyncMergePolicy.ShouldApply(Noon, berlin));
    }

    [Fact]
    public void RepeatedApplicationConverges()
    {
        DateTimeOffset stored = Noon;
        DateTimeOffset incoming = Noon.AddMinutes(5);

        if (SyncMergePolicy.ShouldApply(incoming, stored))
        {
            stored = incoming;
        }

        Assert.False(SyncMergePolicy.ShouldApply(incoming, stored));
        Assert.Equal(incoming, stored);
    }
}
