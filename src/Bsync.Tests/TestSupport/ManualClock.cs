using Bsync.Clocks;

namespace Bsync.Tests.TestSupport;

/// <summary>A controllable physical clock for deterministic HLC tests.</summary>
public sealed class ManualClock : IPhysicalClock
{
    private long _now;

    public ManualClock(long startMilliseconds = 1_000) => _now = startMilliseconds;

    public long NowMilliseconds() => _now;

    /// <summary>Advances the clock by <paramref name="milliseconds"/>.</summary>
    public void Advance(long milliseconds) => _now += milliseconds;

    /// <summary>Sets the clock to an absolute value.</summary>
    public void Set(long milliseconds) => _now = milliseconds;
}
