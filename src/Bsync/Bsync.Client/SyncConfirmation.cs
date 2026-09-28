namespace Bsync.Client;

/// <summary>How far a write is confirmed.</summary>
public enum SyncConfirmation
{
    /// <summary>Committed to the local replica and queued for upload; the server has not seen it yet.</summary>
    SavedLocally = 0,

    /// <summary>The server accepted it.</summary>
    AcceptedByServer = 1,

    /// <summary>The server has a newer version; nothing was written. Reload and retry.</summary>
    Conflict = 2,

    /// <summary>The server refused it (validation, authorization); see <see cref="SyncWriteResult.Message"/>.</summary>
    Rejected = 3,

    /// <summary>The document does not exist.</summary>
    NotFound = 4,
}
