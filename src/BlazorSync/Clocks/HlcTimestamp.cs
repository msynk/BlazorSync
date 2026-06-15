using System.Globalization;

namespace BlazorSync.Clocks;

/// <summary>
/// An immutable Hybrid Logical Clock (HLC) timestamp.
/// <para>
/// Combines a physical wall-clock component (<see cref="WallTime"/>, Unix milliseconds) with a
/// monotonic logical <see cref="Counter"/> and a <see cref="Node"/> identifier. This gives
/// causally-ordered, monotonic timestamps that remain meaningful even when device clocks drift,
/// which is the basis for deterministic conflict resolution across offline clients.
/// </para>
/// </summary>
public readonly record struct HlcTimestamp(long WallTime, int Counter, string Node)
    : IComparable<HlcTimestamp>
{
    /// <summary>The zero/minimum timestamp, used as the starting checkpoint for a full sync.</summary>
    public static readonly HlcTimestamp MinValue = new(0, 0, string.Empty);

    /// <summary>
    /// Compares two timestamps for total ordering: first by wall time, then by logical counter,
    /// then by node id. Including the node guarantees a deterministic total order across devices,
    /// so all peers resolve ties identically.
    /// </summary>
    public int CompareTo(HlcTimestamp other)
    {
        var byWall = WallTime.CompareTo(other.WallTime);
        if (byWall != 0)
        {
            return byWall;
        }

        var byCounter = Counter.CompareTo(other.Counter);
        return byCounter != 0
            ? byCounter
            : string.CompareOrdinal(Node, other.Node);
    }

    /// <summary>Returns true if <paramref name="left"/> orders before <paramref name="right"/>.</summary>
    public static bool operator <(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) < 0;

    /// <summary>Returns true if <paramref name="left"/> orders after <paramref name="right"/>.</summary>
    public static bool operator >(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) > 0;

    /// <summary>Returns true if <paramref name="left"/> orders at or before <paramref name="right"/>.</summary>
    public static bool operator <=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) <= 0;

    /// <summary>Returns true if <paramref name="left"/> orders at or after <paramref name="right"/>.</summary>
    public static bool operator >=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Encodes the timestamp as a lexicographically sortable string of the form
    /// <c>{wallTime:D15}:{counter:D6}:{node}</c>. Lexical ordering of the encoded form matches
    /// chronological ordering, which makes it usable directly as a sortable database column and
    /// as a sync checkpoint cursor.
    /// </summary>
    public string Encode() =>
        string.Create(CultureInfo.InvariantCulture, $"{WallTime:D15}:{Counter:D6}:{Node}");

    /// <summary>Parses a timestamp previously produced by <see cref="Encode"/>.</summary>
    public static HlcTimestamp Parse(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var parts = encoded.Split(':', 3);
        if (parts.Length != 3)
        {
            throw new FormatException($"Invalid HLC timestamp: '{encoded}'.");
        }

        return new HlcTimestamp(
            long.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            parts[2]);
    }

    /// <inheritdoc />
    public override string ToString() => Encode();
}
