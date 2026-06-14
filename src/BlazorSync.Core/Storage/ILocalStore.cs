namespace BlazorSync.Core.Storage;

/// <summary>
/// The local persistence contract for a single synchronized collection. Implementations are the
/// platform-specific storage layer (native SQLite via EF Core, WASM OPFS SQLite, IndexedDB, or the
/// in-memory store used for testing). The store is deliberately mechanical: all protocol and
/// conflict logic lives in the <c>SyncEngine</c>, so a new platform only needs to implement this
/// key/value-with-metadata surface.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ILocalStore<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Returns the stored record for <paramref name="id"/>, or <see langword="null"/> if absent.</summary>
    Task<SyncRecord<TDocument>?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces the record with the supplied state.</summary>
    Task UpsertAsync(SyncRecord<TDocument> record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> dirty records (the push queue), ordered by their
    /// current <see cref="ISyncEntity.UpdatedAt"/> so writes are pushed in causal order.
    /// </summary>
    Task<IReadOnlyList<SyncRecord<TDocument>>> GetDirtyAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the app-visible documents. By default soft-deleted records are excluded; pass
    /// <paramref name="includeDeleted"/> to include them.
    /// </summary>
    Task<IReadOnlyList<TDocument>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the pull checkpoint (resume position) for this collection.</summary>
    Task<Checkpoint> GetCheckpointAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the pull <paramref name="checkpoint"/> for this collection.</summary>
    Task SetCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default);
}
