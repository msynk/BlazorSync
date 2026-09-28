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

/// <summary>Default <see cref="IPhysicalClock"/> backed by the system UTC clock.</summary>
public sealed class SystemPhysicalClock : IPhysicalClock
{
    /// <summary>A shared, stateless instance.</summary>
    public static readonly SystemPhysicalClock Instance = new();

    /// <inheritdoc />
    public long NowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>Thrown when a remote timestamp is further ahead of local physical time than allowed.</summary>
public sealed class ClockDriftException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public ClockDriftException(HlcTimestamp remote, long physicalNow, TimeSpan maxForwardDrift)
        : base($"Remote timestamp {remote} is more than {maxForwardDrift} ahead of local physical time {physicalNow}.")
    {
        Remote = remote;
    }

    /// <summary>The rejected remote timestamp.</summary>
    public HlcTimestamp Remote { get; }
}

/// <summary>
/// A thread-safe Hybrid Logical Clock implementing the algorithm of Kulkarni et al.
/// <para>
/// Call <see cref="Now"/> when generating a local event (for example stamping a write) and
/// <see cref="Update"/> when a timestamp is received from a remote peer. Generated timestamps are
/// strictly monotonic for the lifetime of the instance, even if the physical clock jumps backward.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Counter overflow.</b> The logical counter is bounded by <see cref="HlcTimestamp.MaxCounter"/>.
/// When an event would exceed it, the clock advances its wall component by one millisecond and resets
/// the counter, which preserves strict monotonicity at the cost of running slightly ahead of physical
/// time.
/// </para>
/// <para>
/// <b>Restart.</b> Monotonicity across process restarts requires seeding a new instance with the
/// greatest timestamp it may previously have issued (the <em>high-water mark</em>), either through the
/// constructor or by calling <see cref="Update"/>. <c>SyncEngine</c> does this automatically from its
/// store before its first write.
/// </para>
/// </remarks>
public sealed class HybridLogicalClock
{
    private readonly IPhysicalClock _physical;
    private readonly TimeSpan? _maxForwardDrift;
    private readonly object _gate = new();
    private long _wallTime;
    private int _counter;

    /// <summary>
    /// Creates a clock for the given <paramref name="node"/>. The node id should be stable and
    /// unique per replica (for example a persisted GUID in "N" format).
    /// </summary>
    /// <param name="node">A valid node id (see <see cref="HlcTimestamp.IsValidNode"/>).</param>
    /// <param name="physicalClock">The physical time source; defaults to the system clock.</param>
    /// <param name="highWaterMark">
    /// The greatest timestamp this node may have issued before a restart. Subsequent timestamps are
    /// strictly greater.
    /// </param>
    /// <param name="maxForwardDrift">
    /// When set, <see cref="Update"/> throws <see cref="ClockDriftException"/> for remote timestamps
    /// further than this ahead of local physical time, rather than dragging the clock forward.
    /// </param>
    public HybridLogicalClock(
        string node,
        IPhysicalClock? physicalClock = null,
        HlcTimestamp? highWaterMark = null,
        TimeSpan? maxForwardDrift = null)
    {
        if (!HlcTimestamp.IsValidNode(node))
        {
            throw new ArgumentException(
                $"HLC node ids must be 1-{HlcTimestamp.MaxNodeLength} characters from [A-Za-z0-9._~-].", nameof(node));
        }

        if (maxForwardDrift is { } drift)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(drift, TimeSpan.Zero, nameof(maxForwardDrift));
        }

        Node = node;
        _physical = physicalClock ?? SystemPhysicalClock.Instance;
        _maxForwardDrift = maxForwardDrift;
        if (highWaterMark is { } seed)
        {
            _wallTime = seed.WallTime;
            _counter = seed.Counter;
        }
    }

    /// <summary>Identifies this clock's node; used as the final tie-breaker in timestamp ordering.</summary>
    public string Node { get; }

    /// <summary>The most recent state of the clock (the greatest timestamp issued or observed so far).</summary>
    public HlcTimestamp Last
    {
        get
        {
            lock (_gate)
            {
                return new HlcTimestamp(_wallTime, _counter, Node);
            }
        }
    }

    /// <summary>Generates the next timestamp for a local event.</summary>
    public HlcTimestamp Now()
    {
        lock (_gate)
        {
            var physicalNow = _physical.NowMilliseconds();
            var newWall = Math.Max(_wallTime, physicalNow);
            var newCounter = newWall == _wallTime ? (long)_counter + 1 : 0;
            return Commit(newWall, newCounter);
        }
    }

    /// <summary>
    /// Advances the clock on receipt of a <paramref name="remote"/> timestamp and returns a new
    /// local timestamp that causally follows both the local state and the remote event.
    /// </summary>
    /// <exception cref="ClockDriftException">
    /// A maximum forward drift is configured and <paramref name="remote"/> exceeds it.
    /// </exception>
    public HlcTimestamp Update(HlcTimestamp remote)
    {
        lock (_gate)
        {
            var physicalNow = _physical.NowMilliseconds();
            if (_maxForwardDrift is { } drift && remote.WallTime - physicalNow > (long)drift.TotalMilliseconds)
            {
                throw new ClockDriftException(remote, physicalNow, drift);
            }

            var lastWall = _wallTime;
            var newWall = Math.Max(Math.Max(lastWall, remote.WallTime), physicalNow);

            long newCounter;
            if (newWall == lastWall && newWall == remote.WallTime)
            {
                newCounter = (long)Math.Max(_counter, remote.Counter) + 1;
            }
            else if (newWall == lastWall)
            {
                newCounter = (long)_counter + 1;
            }
            else if (newWall == remote.WallTime)
            {
                newCounter = (long)remote.Counter + 1;
            }
            else
            {
                newCounter = 0;
            }

            return Commit(newWall, newCounter);
        }
    }

    private HlcTimestamp Commit(long wall, long counter)
    {
        if (counter > HlcTimestamp.MaxCounter)
        {
            // Borrow from the physical component rather than overflow the logical one.
            wall++;
            counter = 0;
        }

        if (wall > HlcTimestamp.MaxWallTime)
        {
            throw new OverflowException("The hybrid logical clock has exceeded its maximum wall time.");
        }

        _wallTime = wall;
        _counter = (int)counter;
        return new HlcTimestamp(_wallTime, _counter, Node);
    }
}
