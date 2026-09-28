namespace Bsync.Clocks;

/// <summary>Default <see cref="IPhysicalClock"/> backed by the system UTC clock.</summary>
public sealed class SystemPhysicalClock : IPhysicalClock
{
    /// <summary>A shared, stateless instance.</summary>
    public static readonly SystemPhysicalClock Instance = new();

    /// <inheritdoc />
    public long NowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
