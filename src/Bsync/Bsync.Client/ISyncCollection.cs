namespace Bsync.Blazor;

/// <summary>
/// The component-facing API for one synchronized collection. Components depend only on this interface, so
/// the same component works in every render mode; what differs between hosts is described by
/// <see cref="Capabilities"/> and by the confirmation level of each write.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ISyncCollection<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>What this host can do (offline writes, server-confirmed writes, live updates).</summary>
    SyncCapabilities Capabilities { get; }

    /// <summary>The current replication status.</summary>
    SyncStatus Status { get; }

    /// <summary>Returns the document, or <see langword="null"/> if it does not exist or is deleted.</summary>
    Task<TDocument?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns documents matching <paramref name="query"/>, bounded by <see cref="SyncQuery{TDocument}.Limit"/>. The
    /// filter and order run in memory (they are not translated to a database query). With the default order (by id), local
    /// replicas read the store in index order page by page and stop at the limit; a custom order reads the whole
    /// collection.
    /// </summary>
    Task<IReadOnlyList<TDocument>> QueryAsync(SyncQuery<TDocument>? query = null, CancellationToken cancellationToken = default);

    /// <summary>Returns where one document stands (synced, pending, rejected), or <see langword="null"/> if unknown.</summary>
    Task<SyncItemStatus?> GetItemStatusAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a document. The result says how far the write is confirmed.</summary>
    Task<SyncWriteResult> SaveAsync(TDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves several documents as one dependency group: the server applies them all or none (for example an order and its
    /// lines). Local replicas commit the group at once and upload it in one request; server-connected hosts write it in one
    /// request. Set <see cref="ISyncEntity.Deleted"/> on a document to delete it as part of the group.
    /// </summary>
    Task<IReadOnlyList<SyncWriteResult>> SaveAllAsync(IReadOnlyList<TDocument> documents, CancellationToken cancellationToken = default);

    /// <summary>Deletes a document (a tombstone that replicates).</summary>
    Task<SyncWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> documents whose local change conflicted with a newer server change
    /// and was kept for a decision (the default conflict policy). Hosts without a local replica return an empty list:
    /// their writes report <see cref="SyncConfirmation.Conflict"/> immediately instead.
    /// </summary>
    Task<IReadOnlyList<SyncDocumentConflict<TDocument>>> GetConflictsAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a kept conflict: <paramref name="resolved"/> becomes a new local change based on the newest server
    /// state known locally, and is uploaded like any other write. Returns <see cref="SyncConfirmation.NotFound"/> if
    /// the document has no unresolved conflict.
    /// </summary>
    Task<SyncWriteResult> ResolveConflictAsync(string id, TDocument resolved, CancellationToken cancellationToken = default);

    /// <summary>Drops the kept local change of a conflict; the server state stays. Returns whether a conflict existed.</summary>
    Task<bool> DiscardConflictAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a rejected change again as a new write (after the cause was fixed, for example a permission or the
    /// device clock). Returns <see cref="SyncConfirmation.NotFound"/> if the document is not rejected.
    /// </summary>
    Task<SyncWriteResult> RetryAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the unsynchronized change of a document (pending or rejected) and returns it to the newest server
    /// state known. Returns whether there was a change to discard. An upload already in flight cannot be recalled.
    /// </summary>
    Task<bool> RevertAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <paramref name="onChanged"/> when documents or <see cref="Status"/> may have changed. The callback
    /// may run on any thread; components should call <c>InvokeAsync(StateHasChanged)</c> and re-query.
    /// Dispose the result (for example in the component's <c>Dispose</c>) to stop receiving calls.
    /// </summary>
    IDisposable Subscribe(Action onChanged);
}

/// <summary>A bounded, in-memory query over a collection.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record SyncQuery<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>The largest allowed <see cref="Limit"/>.</summary>
    public const int MaxLimit = 1000;

    /// <summary>Keeps only matching documents. Default: all.</summary>
    public Func<TDocument, bool>? Where { get; init; }

    /// <summary>Sort order. Default: by id (ordinal).</summary>
    public Comparison<TDocument>? Order { get; init; }

    /// <summary>Maximum number of documents returned (1 to <see cref="MaxLimit"/>). Default 100.</summary>
    public int Limit { get; init; } = 100;
}

/// <summary>What a host can do.</summary>
/// <param name="Host">A short description: <c>browser</c>, <c>native</c> or <c>server</c>.</param>
/// <param name="DurableOfflineWrites">Writes are kept on the device and uploaded later if the server is unreachable.</param>
/// <param name="WritesConfirmedByServer">A successful write has already been accepted by the server.</param>
/// <param name="LiveUpdates">Changes made elsewhere are announced without polling (announcements may still be missed).</param>
public sealed record SyncCapabilities(string Host, bool DurableOfflineWrites, bool WritesConfirmedByServer, bool LiveUpdates);

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

/// <summary>A local change kept after it conflicted with a newer server change.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Id">The document id.</param>
/// <param name="Local">The local change that was not applied (a tombstone if it was a delete).</param>
/// <param name="Server">The server state the change conflicted with (a tombstone if the server deleted it). The server may have moved on since; <see cref="ISyncCollection{TDocument}.GetAsync"/> returns the newest known state.</param>
/// <param name="Base">The server state the local change was based on, if known; useful for three-way merges.</param>
public sealed record SyncDocumentConflict<TDocument>(string Id, TDocument Local, TDocument Server, TDocument? Base)
    where TDocument : class, ISyncEntity;

/// <summary>The synchronization status of one document.</summary>
/// <param name="State">The state.</param>
/// <param name="Detail">The rejection code, when <see cref="SyncItemState.Rejected"/>.</param>
public sealed record SyncItemStatus(SyncItemState State, string? Detail = null);

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

/// <summary>The result of a write.</summary>
/// <param name="Id">The document id.</param>
/// <param name="Confirmation">How far the write is confirmed.</param>
/// <param name="Message">An error code or explanation for <see cref="SyncConfirmation.Rejected"/> and <see cref="SyncConfirmation.Conflict"/>.</param>
public sealed record SyncWriteResult(string Id, SyncConfirmation Confirmation, string? Message = null)
{
    /// <summary>Whether the write took effect (locally or on the server).</summary>
    public bool Succeeded => Confirmation is SyncConfirmation.SavedLocally or SyncConfirmation.AcceptedByServer;
}

/// <summary>Shared query evaluation.</summary>
internal static class Queries
{
    public const int PageSize = 200;

    public static void Validate<TDocument>(SyncQuery<TDocument> query)
        where TDocument : class, ISyncEntity
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Limit, 1, nameof(query.Limit));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Limit, SyncQuery<TDocument>.MaxLimit, nameof(query.Limit));
    }

    public static IReadOnlyList<TDocument> Apply<TDocument>(IEnumerable<TDocument> documents, SyncQuery<TDocument>? query)
        where TDocument : class, ISyncEntity
    {
        query ??= new SyncQuery<TDocument>();
        Validate(query);

        var matching = documents.Where(d => !d.Deleted && (query.Where?.Invoke(d) ?? true)).ToList();
        matching.Sort(query.Order ?? ((a, b) => string.CompareOrdinal(a.Id, b.Id)));
        return matching.Count > query.Limit ? matching.GetRange(0, query.Limit) : matching;
    }
}
