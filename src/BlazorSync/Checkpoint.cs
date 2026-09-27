namespace BlazorSync;

/// <summary>
/// An opaque, server-issued resume position in a collection's change feed.
/// </summary>
/// <remarks>
/// Clients store and return checkpoints verbatim and must not parse, compare or construct them. The
/// server encodes whatever it needs to prove that everything up to the position is a committed,
/// gap-free prefix of the feed (for example an epoch and a commit sequence). A checkpoint is only
/// meaningful to the server and scope that issued it.
/// </remarks>
/// <param name="Value">The opaque token, or <see langword="null"/> for <see cref="Start"/>.</param>
public readonly record struct Checkpoint(string? Value)
{
    /// <summary>The position before the first change (a full sync).</summary>
    public static readonly Checkpoint Start = default;

    /// <summary>Whether this is <see cref="Start"/>.</summary>
    public bool IsStart => Value is null;

    /// <inheritdoc />
    public override string ToString() => Value ?? "(start)";
}
