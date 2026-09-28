namespace Bsync.Client;

/// <summary>The synchronization status of one document.</summary>
/// <param name="State">The state.</param>
/// <param name="Detail">The rejection code, when <see cref="SyncItemState.Rejected"/>.</param>
public sealed record SyncItemStatus(SyncItemState State, string? Detail = null);
