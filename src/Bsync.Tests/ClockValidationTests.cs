using Bsync.Clocks;
using Bsync.Tests.TestSupport;
using Xunit;

namespace Bsync.Tests;

/// <summary>HLC field validation, canonical encoding, overflow and restart behaviour (I12).</summary>
public sealed class ClockValidationTests
{
    [Theory(DisplayName = "T22 I12: malformed encodings are rejected")]
    [InlineData("")]
    [InlineData("1:2:3")]
    [InlineData("00000000000100:000001:node")]      // 14-digit wall
    [InlineData("000000000001000:00001:node")]      // 5-digit counter
    [InlineData("+00000000001000:000001:node")]
    [InlineData(" 00000000001000:000001:node")]
    [InlineData("000000000001000:000001:no de")]
    [InlineData("000000000001000:000001:nöde")]
    [InlineData("000000000001000:-00001:node")]
    public void MalformedEncodingsRejected(string encoded)
    {
        Assert.False(HlcTimestamp.TryParse(encoded, out _));
        Assert.Throws<FormatException>(() => HlcTimestamp.Parse(encoded));
    }

    [Theory(DisplayName = "T22 I12: out-of-range fields are rejected")]
    [InlineData(-1L, 0, "n")]
    [InlineData(HlcTimestamp.MaxWallTime + 1, 0, "n")]
    [InlineData(0L, -1, "n")]
    [InlineData(0L, HlcTimestamp.MaxCounter + 1, "n")]
    public void OutOfRangeFieldsRejected(long wall, int counter, string node)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HlcTimestamp(wall, counter, node));
    }

    [Theory(DisplayName = "T22 I12: invalid node ids are rejected")]
    [InlineData("has space")]
    [InlineData("colon:node")]
    [InlineData("ünïcode")]
    public void InvalidNodesRejected(string node)
    {
        Assert.Throws<ArgumentException>(() => new HlcTimestamp(1, 0, node));
        Assert.Throws<ArgumentException>(() => new HybridLogicalClock(node));
    }

    [Fact(DisplayName = "I12: an empty node is valid only for timestamps, and default equals MinValue")]
    public void DefaultEqualsMinValue()
    {
        Assert.Equal(HlcTimestamp.MinValue, default);
        Assert.Equal(HlcTimestamp.MinValue, new HlcTimestamp(0, 0, string.Empty));
        Assert.Equal(HlcTimestamp.MinValue, HlcTimestamp.Parse(default(HlcTimestamp).Encode()));
        Assert.Throws<ArgumentException>(() => new HybridLogicalClock(string.Empty));
    }

    [Theory(DisplayName = "T24 I12: encoded ordinal order equals numeric order (seeded property)")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EncodedOrderMatchesCompareTo(int seed)
    {
        var random = new Random(seed);
        string[] nodes = ["a", "a-b", "a.b", "A", "z", "0", "~", "node-01", "node-1"];
        var stamps = Enumerable.Range(0, 400).Select(_ => new HlcTimestamp(
            random.Next(3) == 0 ? random.NextInt64(0, HlcTimestamp.MaxWallTime + 1) : random.Next(1_000, 1_004),
            random.Next(3) == 0 ? random.Next(0, HlcTimestamp.MaxCounter + 1) : random.Next(0, 3),
            nodes[random.Next(nodes.Length)])).ToList();

        foreach (var x in stamps)
        {
            Assert.Equal(x, HlcTimestamp.Parse(x.Encode()));
            foreach (var y in stamps.Take(40))
            {
                Assert.Equal(Math.Sign(x.CompareTo(y)), Math.Sign(string.CompareOrdinal(x.Encode(), y.Encode())));
            }
        }
    }

    [Fact(DisplayName = "T21 I12: Now stays monotonic across counter overflow with a frozen physical clock")]
    public void NowCarriesOverflowIntoWallTime()
    {
        var clock = new HybridLogicalClock("a", new ManualClock(1_000), highWaterMark: new HlcTimestamp(1_000, HlcTimestamp.MaxCounter - 1, "a"));

        var a = clock.Now();
        var b = clock.Now();

        Assert.Equal(new HlcTimestamp(1_000, HlcTimestamp.MaxCounter, "a"), a);
        Assert.Equal(new HlcTimestamp(1_001, 0, "a"), b);
    }

    [Fact(DisplayName = "T23 I12: a seeded clock issues timestamps above its high-water mark")]
    public void SeededClockIsAboveHighWater()
    {
        var seed = new HlcTimestamp(5_000, 7, "a");
        var clock = new HybridLogicalClock("a", new ManualClock(1_000), highWaterMark: seed);
        Assert.True(clock.Now() > seed);
        Assert.True(clock.Last > seed);
    }

    [Fact(DisplayName = "T21 I12: a configured drift bound rejects far-future remote timestamps")]
    public void DriftBound()
    {
        var clock = new HybridLogicalClock("a", new ManualClock(1_000), maxForwardDrift: TimeSpan.FromSeconds(1));

        Assert.Throws<ClockDriftException>(() => clock.Update(new HlcTimestamp(10_000, 0, "b")));
        Assert.Equal(0, clock.Last.WallTime); // state unchanged
        Assert.Equal(1_500, clock.Update(new HlcTimestamp(1_500, 0, "b")).WallTime);
    }

    [Fact(DisplayName = "T21 I12: a backward physical jump does not regress")]
    public void BackwardJump()
    {
        var physical = new ManualClock(5_000);
        var clock = new HybridLogicalClock("a", physical);
        var first = clock.Now();
        physical.Set(1);
        Assert.True(clock.Now() > first);
    }

    [Fact(DisplayName = "I12: timestamps round-trip through the default JSON cloner")]
    public void JsonRoundTrip()
    {
        var note = new Note { Id = "n", UpdatedAt = new HlcTimestamp(123, 4, "node-x") };
#pragma warning disable IL2026, IL3050
        var copy = Documents.DocumentCloner.JsonClone(note);
#pragma warning restore IL2026, IL3050
        Assert.Equal(note.UpdatedAt, copy.UpdatedAt);
    }
}
