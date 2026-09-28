using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Bsync.Conflicts;

/// <summary>Semantic merge rules for members that are sets or counters, named by JSON Pointer (for example <c>/Tags</c>).</summary>
/// <remarks>
/// A set member is a JSON array whose items are compared by value: items added on either side are kept, items removed on
/// either side are removed, and duplicates collapse. A counter member is a number: the result is
/// <c>server + (local - base)</c>, so concurrent increments add up. Use the member names as they appear in JSON (after
/// any naming policy).
/// </remarks>
public sealed record ThreeWayMergeOptions
{
    /// <summary>No semantic members: every array and number is an atomic value.</summary>
    public static ThreeWayMergeOptions Default { get; } = new();

    /// <summary>JSON Pointers of members merged as sets.</summary>
    public IReadOnlySet<string> Sets { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>JSON Pointers of members merged as counters.</summary>
    public IReadOnlySet<string> Counters { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
