namespace Bsync.Client;

/// <summary>Where one document stands relative to the server.</summary>
public enum SyncItemState
{
    /// <summary>The local state is the last state confirmed by the server.</summary>
    Synced = 0,

    /// <summary>Local changes are waiting to be uploaded or confirmed.</summary>
    Pending = 1,

    /// <summary>The server refused the latest local change; see <see cref="SyncItemStatus.Detail"/>. Edit the document again to retry.</summary>
    Rejected = 2,

    /// <summary>The server no longer has this document after a reset; it is hidden from queries.</summary>
    MissingAfterReset = 3,

    /// <summary>
    /// A local change conflicted with a newer server change and is kept for a decision; the document shows the
    /// server state. See <see cref="ISyncCollection{TDocument}.GetConflictsAsync"/>.
    /// </summary>
    Conflicted = 4,
}
