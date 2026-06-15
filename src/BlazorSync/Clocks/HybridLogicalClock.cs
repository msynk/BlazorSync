namespace BlazorSync.Clocks;

/// <summary>
/// Provides the current wall-clock time in Unix milliseconds. Abstracted so tests can supply a
/// deterministic clock and so platforms with a constrained time source can substitute their own.
/// </summary>
public interface IPhysicalClock
{
    /// <summary>Returns the current time in Unix milliseconds.</summary>
    long NowMilliseconds();
}

/// <summary>Default <see cref="IPhysicalClock"/> backed by the system UTC clock.</summary>
public sealed class SystemPhysicalClock : IPhysicalClock
{
    /// <summary>A shared, stateless instance.</summary>
    public static readonly SystemPhysicalClock Instance = new();

    /// <inheritdoc />
    public long NowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// A thread-safe Hybrid Logical Clock implementing the algorithm of Kulkarni et al.
/// <para>
/// Call <see cref="Now"/> when generating a local event (for example stamping a write) and
/// <see cref="Update"/> when a timestamp is received from a remote peer. The clock guarantees that
/// generated timestamps are strictly monotonic and never move backwards, even if the physical
/// clock jumps backward or a remote timestamp is ahead of local time.
/// </para>
/// </summary>
public sealed class HybridLogicalClock
{
    private readonly IPhysicalClock _physical;
    private readonly object _gate = new();
    private long _wallTime;
    private int _counter;

    /// <summary>Identifies this clock's node; used as the final tie-breaker in timestamp ordering.</summary>
    public string Node { get; }

    /// <summary>
    /// Creates a clock for the given <paramref name="node"/>. The node id should be stable and
    /// unique per device/installation (for example a persisted GUID).
    /// </summary>
    public HybridLogicalClock(string node, IPhysicalClock? physicalClock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(node);
        Node = node;
        _physical = physicalClock ?? SystemPhysicalClock.Instance;
    }

    /// <summary>Generates the next timestamp for a local event.</summary>
    public HlcTimestamp Now()
    {
        lock (_gate)
        {
            var physicalNow = _physical.NowMilliseconds();
            var lastWall = _wallTime;

            _wallTime = Math.Max(lastWall, physicalNow);
            _counter = _wallTime == lastWall ? _counter + 1 : 0;

            return new HlcTimestamp(_wallTime, _counter, Node);
        }
    }

    /// <summary>
    /// Advances the clock on receipt of a <paramref name="remote"/> timestamp and returns a new
    /// local timestamp that causally follows both the local state and the remote event.
    /// </summary>
    public HlcTimestamp Update(HlcTimestamp remote)
    {
        lock (_gate)
        {
            var physicalNow = _physical.NowMilliseconds();
            var lastWall = _wallTime;
            var lastCounter = _counter;

            var newWall = Math.Max(Math.Max(lastWall, remote.WallTime), physicalNow);

            if (newWall == lastWall && newWall == remote.WallTime)
            {
                _counter = Math.Max(lastCounter, remote.Counter) + 1;
            }
            else if (newWall == lastWall)
            {
                _counter = lastCounter + 1;
            }
            else if (newWall == remote.WallTime)
            {
                _counter = remote.Counter + 1;
            }
            else
            {
                _counter = 0;
            }

            _wallTime = newWall;
            return new HlcTimestamp(_wallTime, _counter, Node);
        }
    }
}
