namespace Bsync.Client;

/// <summary>Replication state of a collection.</summary>
public enum SyncState
{
    /// <summary>Opening the local replica or connecting.</summary>
    Starting = 0,

    /// <summary>Nothing known to be pending; the last sync reached the server's then-current state.</summary>
    Synced = 1,

    /// <summary>A sync is running or more work is queued.</summary>
    Syncing = 2,

    /// <summary>The server is unreachable; local writes are kept and retried with backoff.</summary>
    Offline = 3,

    /// <summary>Another tab owns replication for this replica; this tab reads and writes locally.</summary>
    Follower = 4,

    /// <summary>Sync stopped for a reason that needs the user or the app (sign-in, upgrade, rejected writes, storage).</summary>
    AttentionRequired = 5,

    /// <summary>The session was stopped or disposed.</summary>
    Stopped = 6,

    /// <summary>Replication is paused (for example while a native app is in the background); local work continues.</summary>
    Paused = 7,
}
