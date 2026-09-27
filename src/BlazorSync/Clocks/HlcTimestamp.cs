using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace BlazorSync.Clocks;

/// <summary>
/// An immutable Hybrid Logical Clock (HLC) timestamp.
/// <para>
/// Combines a physical wall-clock component (<see cref="WallTime"/>, Unix milliseconds) with a
/// bounded logical <see cref="Counter"/> and a <see cref="Node"/> identifier. Every field is
/// validated on construction so that the numeric ordering (<see cref="CompareTo"/>) and the ordinal
/// ordering of the <see cref="Encode"/>d form always agree.
/// </para>
/// </summary>
/// <remarks>
/// In BlazorSync an HLC timestamp is <em>origin metadata</em>: it records when and where a write was
/// authored. It is not the server's concurrency token or the pull cursor; those are the server
/// document version and the opaque <see cref="Checkpoint"/> respectively.
/// </remarks>
[JsonConverter(typeof(Protocol.HlcTimestampJsonConverter))]
public readonly record struct HlcTimestamp : IComparable<HlcTimestamp>
{
    /// <summary>The largest valid <see cref="WallTime"/> (15 decimal digits, about year 33658).</summary>
    public const long MaxWallTime = 999_999_999_999_999;

    /// <summary>The largest valid <see cref="Counter"/> (6 decimal digits).</summary>
    public const int MaxCounter = 999_999;

    /// <summary>The maximum length of a <see cref="Node"/> identifier.</summary>
    public const int MaxNodeLength = 64;

    /// <summary>The zero/minimum timestamp. Equal to <c>default(HlcTimestamp)</c>.</summary>
    public static readonly HlcTimestamp MinValue = default;

    private readonly string? _node;

    /// <summary>Creates a validated timestamp.</summary>
    /// <param name="wallTime">Unix milliseconds in <c>[0, <see cref="MaxWallTime"/>]</c>.</param>
    /// <param name="counter">Logical counter in <c>[0, <see cref="MaxCounter"/>]</c>.</param>
    /// <param name="node">
    /// Node identifier: empty, or 1–64 characters from <c>[A-Za-z0-9._~-]</c>. The restricted
    /// alphabet makes ordinal, UTF-8 byte and common database collation orders identical.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A numeric field is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> is invalid.</exception>
    public HlcTimestamp(long wallTime, int counter, string node)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wallTime);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(wallTime, MaxWallTime);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, MaxCounter);
        ArgumentNullException.ThrowIfNull(node);
        if (node.Length != 0 && !IsValidNode(node))
        {
            throw new ArgumentException(
                $"HLC node ids must be 1-{MaxNodeLength} characters from [A-Za-z0-9._~-].", nameof(node));
        }

        WallTime = wallTime;
        Counter = counter;
        _node = node;
    }

    /// <summary>Physical component, Unix milliseconds.</summary>
    public long WallTime { get; }

    /// <summary>Logical component, used to order events within the same millisecond.</summary>
    public int Counter { get; }

    /// <summary>The node that issued the timestamp; the final tie-breaker in ordering.</summary>
    public string Node => _node ?? string.Empty;

    /// <summary>Returns <see langword="true"/> when <paramref name="node"/> is a valid, non-empty node id.</summary>
    public static bool IsValidNode([NotNullWhen(true)] string? node)
    {
        if (string.IsNullOrEmpty(node) || node.Length > MaxNodeLength)
        {
            return false;
        }

        foreach (var c in node)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Deconstructs the timestamp into its components.</summary>
    public void Deconstruct(out long wallTime, out int counter, out string node)
    {
        wallTime = WallTime;
        counter = Counter;
        node = Node;
    }

    /// <summary>
    /// Compares two timestamps for total ordering: wall time, then logical counter, then node id
    /// (ordinal). All peers therefore order any two timestamps identically.
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

    /// <inheritdoc />
    public bool Equals(HlcTimestamp other) =>
        WallTime == other.WallTime && Counter == other.Counter && string.Equals(Node, other.Node, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(WallTime, Counter, StringComparer.Ordinal.GetHashCode(Node));

    /// <summary>Returns true if <paramref name="left"/> orders before <paramref name="right"/>.</summary>
    public static bool operator <(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) < 0;

    /// <summary>Returns true if <paramref name="left"/> orders after <paramref name="right"/>.</summary>
    public static bool operator >(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) > 0;

    /// <summary>Returns true if <paramref name="left"/> orders at or before <paramref name="right"/>.</summary>
    public static bool operator <=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) <= 0;

    /// <summary>Returns true if <paramref name="left"/> orders at or after <paramref name="right"/>.</summary>
    public static bool operator >=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Encodes the timestamp as <c>{wallTime:D15}:{counter:D6}:{node}</c>. Because both numeric
    /// fields are fixed width and the node alphabet is ASCII, ordinal comparison of encoded strings
    /// matches <see cref="CompareTo"/> for every valid timestamp.
    /// </summary>
    public string Encode() =>
        string.Create(CultureInfo.InvariantCulture, $"{WallTime:D15}:{Counter:D6}:{Node}");

    /// <summary>Parses a timestamp previously produced by <see cref="Encode"/>.</summary>
    /// <exception cref="FormatException"><paramref name="encoded"/> is not a canonical encoding.</exception>
    public static HlcTimestamp Parse(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        return TryParse(encoded, out var result)
            ? result
            : throw new FormatException($"Invalid HLC timestamp: '{encoded}'.");
    }

    /// <summary>
    /// Parses the canonical encoding strictly: exactly 15 digits, ':', exactly 6 digits, ':', and a
    /// valid (possibly empty) node id. Signs, whitespace and other digit counts are rejected.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? encoded, out HlcTimestamp result)
    {
        result = default;
        if (encoded is null || encoded.Length < 23 || encoded[15] != ':' || encoded[22] != ':')
        {
            return false;
        }

        var wall = encoded.AsSpan(0, 15);
        var counter = encoded.AsSpan(16, 6);
        var node = encoded[23..];
        if (!IsAsciiDigits(wall) || !IsAsciiDigits(counter) || (node.Length != 0 && !IsValidNode(node)))
        {
            return false;
        }

        result = new HlcTimestamp(
            long.Parse(wall, NumberStyles.None, CultureInfo.InvariantCulture),
            int.Parse(counter, NumberStyles.None, CultureInfo.InvariantCulture),
            node);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Encode();

    private static bool IsAsciiDigits(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
