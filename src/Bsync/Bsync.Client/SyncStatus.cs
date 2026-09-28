namespace Bsync.Client;

/// <summary>A snapshot of replication status.</summary>
/// <param name="State">The state.</param>
/// <param name="Pending">Local changes not yet confirmed by the server (0 for server-connected hosts).</param>
/// <param name="Detail">A human-readable explanation for <see cref="SyncState.Offline"/> or <see cref="SyncState.AttentionRequired"/>.</param>
/// <param name="LastSynced">When a sync last completed without remaining work, if ever.</param>
public sealed record SyncStatus(SyncState State, int Pending, string? Detail, DateTimeOffset? LastSynced)
{
    /// <summary>The initial status.</summary>
    public static SyncStatus Starting { get; } = new(SyncState.Starting, 0, null, null);
}
