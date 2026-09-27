using BlazorSync.Clocks;

namespace BlazorSync.Storage;

/// <summary>
/// The local persistence contract for one synchronized collection on one replica. Implementations are
/// the platform-specific storage layer; all protocol and conflict logic lives in the <c>SyncEngine</c>.
/// </summary>
/// <remarks>
/// <para>
/// All writes go through <see cref="UpdateAsync"/>, an atomic compare-and-transform over one or more
/// records plus, optionally, the checkpoint. This is what keeps a local edit made during a network
/// round trip from being overwritten by an acknowledgement or a pull that was computed from an older
/// read (invariants I02 and I03 in <c>docs/architecture/invariants.md</c>).
/// </para>
/// <para>
/// Stores must never hand out references to their internal state: records passed in or returned are
/// independent copies.
/// </para>
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ILocalStore<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Returns the stored record for <paramref name="id"/>, or <see langword="null"/> if absent.</summary>
    Task<SyncRecord<TDocument>?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically applies <paramref name="updates"/> and, when <paramref name="checkpoint"/> is not
    /// <see langword="null"/>, stores it as the new pull checkpoint. Either every change becomes
    /// durable together or none does.
    /// </summary>
    /// <param name="updates">
    /// One entry per distinct record id. Each transform receives an independent copy of the record's
    /// state at commit time (or <see langword="null"/> if absent) and returns the new state, or
    /// <see langword="null"/> to leave the record unchanged. Transforms must be pure and fast, must not
    /// call the store, and may be invoked more than once by optimistic implementations.
    /// </param>
    /// <param name="checkpoint">The pull checkpoint to commit with the updates, if any.</param>
    /// <param name="cancellationToken">Cancels the operation before it commits.</param>
    /// <returns>One result per update, in order.</returns>
    Task<IReadOnlyList<RecordUpdateResult<TDocument>>> UpdateAsync(
        IReadOnlyList<RecordUpdate<TDocument>> updates,
        Checkpoint? checkpoint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> pushable records (<see cref="SyncRecord{TDocument}.IsPushable"/>)
    /// ordered by their current <see cref="ISyncEntity.UpdatedAt"/> then id, skipping ids in
    /// <paramref name="exclude"/>.
    /// </summary>
    Task<IReadOnlyList<SyncRecord<TDocument>>> GetPendingAsync(
        int limit,
        IReadOnlySet<string>? exclude = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the number of dirty records (pushable or rejected).</summary>
    Task<int> CountDirtyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the app-visible documents. By default soft-deleted records are excluded; pass
    /// <paramref name="includeDeleted"/> to include them.
    /// </summary>
    Task<IReadOnlyList<TDocument>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the pull checkpoint (resume position) for this collection.</summary>
    Task<Checkpoint> GetCheckpointAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the greatest <see cref="ISyncEntity.UpdatedAt"/> ever committed to this store, so a
    /// restarted replica can seed its clock above every timestamp it may already have issued.
    /// </summary>
    Task<HlcTimestamp> GetClockHighWaterAsync(CancellationToken cancellationToken = default);
}

/// <summary>A conditional transformation of one record, applied by <see cref="ILocalStore{TDocument}.UpdateAsync"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Id">The record id. The returned record's <c>Current.Id</c> must equal it.</param>
/// <param name="Transform">Maps the committed state to the new state, or to <see langword="null"/> for no change.</param>
public sealed record RecordUpdate<TDocument>(string Id, Func<SyncRecord<TDocument>?, SyncRecord<TDocument>?> Transform)
    where TDocument : class, ISyncEntity;

/// <summary>The outcome of one <see cref="RecordUpdate{TDocument}"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Record">The record's state after the commit (a copy), or <see langword="null"/> if absent.</param>
/// <param name="Changed">Whether the transform produced a new state.</param>
public sealed record RecordUpdateResult<TDocument>(SyncRecord<TDocument>? Record, bool Changed)
    where TDocument : class, ISyncEntity;
