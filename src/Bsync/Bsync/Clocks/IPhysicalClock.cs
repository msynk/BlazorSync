namespace Bsync.Clocks;

/// <summary>
/// Provides the current wall-clock time in Unix milliseconds. Abstracted so tests can supply a
/// deterministic clock and so platforms with a constrained time source can substitute their own.
/// </summary>
public interface IPhysicalClock
{
    /// <summary>Returns the current time in Unix milliseconds.</summary>
    long NowMilliseconds();
}
