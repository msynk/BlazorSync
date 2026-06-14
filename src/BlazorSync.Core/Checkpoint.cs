using BlazorSync.Core.Clocks;

namespace BlazorSync.Core;

/// <summary>
/// A resumable position in a collection's change stream. A checkpoint is the
/// <see cref="HlcTimestamp"/> and <see cref="Id"/> of the last record processed. Because records
/// are ordered deterministically by (<see cref="UpdatedAt"/>, <see cref="Id"/>), a peer can resume
/// pulling from exactly where it left off, with no gaps and no duplicates.
/// </summary>
public readonly record struct Checkpoint(HlcTimestamp UpdatedAt, string Id)
{
    /// <summary>The starting checkpoint representing "the beginning of time" (a full sync).</summary>
    public static readonly Checkpoint Start = new(HlcTimestamp.MinValue, string.Empty);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="entity"/> sorts strictly after this
    /// checkpoint and therefore has not yet been seen by the holder of the checkpoint.
    /// </summary>
    public bool IsBefore(ISyncEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var byTime = UpdatedAt.CompareTo(entity.UpdatedAt);
        return byTime != 0
            ? byTime < 0
            : string.CompareOrdinal(Id, entity.Id) < 0;
    }
}
