using BlazorSync.Core.Documents;

namespace BlazorSync.Core.Storage;

/// <summary>
/// An in-memory <see cref="ILocalStore{TDocument}"/>. Useful for tests, prototypes and ephemeral
/// (non-persisted) scenarios. All stored states are deep-cloned on the way in and out so callers can
/// never accidentally mutate the store's internal copies.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemoryLocalStore<TDocument> : ILocalStore<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly Dictionary<string, SyncRecord<TDocument>> _records = new(StringComparer.Ordinal);
    private readonly Func<TDocument, TDocument> _clone;
    private readonly object _gate = new();
    private Checkpoint _checkpoint = Checkpoint.Start;

    /// <summary>
    /// Creates a store using the supplied deep-clone function. When <paramref name="cloner"/> is
    /// <see langword="null"/> a reflection-based JSON clone is used, which is not trim/AOT safe;
    /// supply an explicit cloner for Blazor WebAssembly publish builds.
    /// </summary>
#pragma warning disable IL2026, IL3050 // Default cloner is reflection-based; suppressed so a supplied cloner is warning-free.
    public InMemoryLocalStore(Func<TDocument, TDocument>? cloner = null)
    {
        _clone = cloner ?? (static doc => DocumentCloner.JsonClone(doc));
    }
#pragma warning restore IL2026, IL3050

    private SyncRecord<TDocument> CloneRecord(SyncRecord<TDocument> record) =>
        new(_clone(record.Current), record.Base is { } b ? _clone(b) : null, record.IsDirty);

    /// <inheritdoc />
    public Task<SyncRecord<TDocument>?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            return Task.FromResult(_records.TryGetValue(id, out var record) ? CloneRecord(record) : null);
        }
    }

    /// <inheritdoc />
    public Task UpsertAsync(SyncRecord<TDocument> record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _records[record.Current.Id] = CloneRecord(record);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncRecord<TDocument>>> GetDirtyAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var dirty = _records.Values
                .Where(static r => r.IsDirty)
                .OrderBy(static r => r.Current.UpdatedAt)
                .Take(batchSize)
                .Select(CloneRecord)
                .ToList();

            return Task.FromResult<IReadOnlyList<SyncRecord<TDocument>>>(dirty);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TDocument>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var documents = _records.Values
                .Where(r => includeDeleted || !r.Current.Deleted)
                .Select(r => _clone(r.Current))
                .ToList();

            return Task.FromResult<IReadOnlyList<TDocument>>(documents);
        }
    }

    /// <inheritdoc />
    public Task<Checkpoint> GetCheckpointAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_checkpoint);
        }
    }

    /// <inheritdoc />
    public Task SetCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _checkpoint = checkpoint;
        }

        return Task.CompletedTask;
    }
}
