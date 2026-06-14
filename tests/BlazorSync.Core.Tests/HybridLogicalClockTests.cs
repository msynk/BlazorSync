using BlazorSync.Core.Clocks;
using BlazorSync.Core.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Core.Tests;

public sealed class HybridLogicalClockTests
{
    [Fact]
    public void Now_IsStrictlyMonotonic_EvenWhenPhysicalClockIsFrozen()
    {
        var physical = new ManualClock(1_000);
        var clock = new HybridLogicalClock("node-a", physical);

        var first = clock.Now();
        var second = clock.Now();
        var third = clock.Now();

        // Physical time frozen, so the logical counter must advance to preserve monotonicity.
        Assert.True(first < second);
        Assert.True(second < third);
        Assert.Equal(1_000, first.WallTime);
        Assert.Equal(0, first.Counter);
        Assert.Equal(1, second.Counter);
        Assert.Equal(2, third.Counter);
    }

    [Fact]
    public void Now_ResetsCounter_WhenPhysicalClockAdvances()
    {
        var physical = new ManualClock(1_000);
        var clock = new HybridLogicalClock("node-a", physical);

        var first = clock.Now();
        physical.Advance(5);
        var second = clock.Now();

        Assert.Equal(0, first.Counter);
        Assert.Equal(1_005, second.WallTime);
        Assert.Equal(0, second.Counter);
    }

    [Fact]
    public void Now_DoesNotGoBackwards_WhenPhysicalClockJumpsBack()
    {
        var physical = new ManualClock(1_000);
        var clock = new HybridLogicalClock("node-a", physical);

        var first = clock.Now();
        physical.Set(500); // clock skew backwards
        var second = clock.Now();

        Assert.True(second > first);
        Assert.Equal(1_000, second.WallTime); // never regresses below the high-water mark
    }

    [Fact]
    public void Update_CausallyFollowsRemoteTimestamp()
    {
        var physical = new ManualClock(1_000);
        var clock = new HybridLogicalClock("node-a", physical);

        var remote = new HlcTimestamp(9_999, 3, "node-b");
        var local = clock.Update(remote);

        Assert.True(local > remote);
        Assert.Equal(9_999, local.WallTime);
        Assert.Equal(4, local.Counter);
        Assert.Equal("node-a", local.Node);
    }

    [Fact]
    public void Encode_RoundTrips_AndSortsChronologically()
    {
        var early = new HlcTimestamp(1_000, 5, "z-node");
        var late = new HlcTimestamp(1_001, 0, "a-node");

        Assert.Equal(early, HlcTimestamp.Parse(early.Encode()));
        Assert.True(string.CompareOrdinal(early.Encode(), late.Encode()) < 0);
    }

    [Fact]
    public void CompareTo_BreaksTiesByNode_ForDeterministicTotalOrder()
    {
        var a = new HlcTimestamp(1_000, 1, "node-a");
        var b = new HlcTimestamp(1_000, 1, "node-b");

        Assert.True(a < b);
    }
}
