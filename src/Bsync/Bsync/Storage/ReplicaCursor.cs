namespace Bsync.Storage;

/// <summary>
/// The replica's position in the authority's history: the pull checkpoint, plus a generation that is
/// incremented each time the replica has to resnapshot (for example after the authority was restored from
/// a backup). Server versions are only compared within one generation.
/// </summary>
/// <param name="Checkpoint">The pull checkpoint.</param>
/// <param name="Generation">The replica's current generation, starting at 0.</param>
/// <param name="Resnapshot">
/// <see langword="true"/> while a full pull after a reset is in progress; records not seen by the time it
/// completes are marked <see cref="SyncRecord{TDocument}.MissingAfterReset"/>.
/// </param>
/// <param name="PurgeMissing">
/// With <paramref name="Resnapshot"/>: remove (rather than hide) clean records the completed snapshot does not
/// contain, because the reset happened for a change of access or retention rather than a server restore.
/// </param>
public readonly record struct ReplicaCursor(Checkpoint Checkpoint, long Generation, bool Resnapshot, bool PurgeMissing = false)
{
    /// <summary>The cursor of a replica that has never pulled.</summary>
    public static readonly ReplicaCursor Initial = default;
}
