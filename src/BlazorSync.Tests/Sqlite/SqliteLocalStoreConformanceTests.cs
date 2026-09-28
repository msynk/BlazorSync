using BlazorSync.Storage;
using BlazorSync.Storage.Sqlite;
using BlazorSync.Testing;
using BlazorSync.Tests.Conformance;

namespace BlazorSync.Tests.Sqlite;

/// <summary>The shared store contract, run against a real SQLite file.</summary>
public sealed class SqliteLocalStoreConformanceTests : LocalStoreConformanceTests, IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    protected override async Task<ILocalStore<ConformanceDocument>> CreateStoreAsync() =>
        await _database.OpenConformanceAsync();

    public void Dispose() => _database.Dispose();
}

/// <summary>The shared store contract with two store instances on one file (writes through one, reads through the other).</summary>
public sealed class SqliteSharedFileConformanceTests : LocalStoreConformanceTests, IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    protected override async Task<ILocalStore<ConformanceDocument>> CreateStoreAsync() =>
        new SplitStore<ConformanceDocument>(await _database.OpenConformanceAsync(), await _database.OpenConformanceAsync());

    public void Dispose() => _database.Dispose();
}

/// <summary>Writes through one store instance and reads through another sharing the same storage.</summary>
public sealed class SplitStore<T>(ILocalStore<T> writer, ILocalStore<T> reader) : ILocalStore<T>
    where T : class, ISyncEntity
{
    public Task<SyncRecord<T>?> GetAsync(string id, CancellationToken cancellationToken = default) => reader.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<RecordUpdateResult<T>>> UpdateAsync(IReadOnlyList<RecordUpdate<T>> updates, ReplicaCursor? cursor = null, CancellationToken cancellationToken = default) =>
        writer.UpdateAsync(updates, cursor, cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetPendingAsync(int limit, IReadOnlySet<string>? exclude = null, CancellationToken cancellationToken = default) =>
        reader.GetPendingAsync(limit, exclude, cancellationToken);

    public Task<int> CountDirtyAsync(CancellationToken cancellationToken = default) => reader.CountDirtyAsync(cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetStaleAsync(long generation, int limit, CancellationToken cancellationToken = default) =>
        reader.GetStaleAsync(generation, limit, cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetConflictsAsync(int limit, CancellationToken cancellationToken = default) =>
        reader.GetConflictsAsync(limit, cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetRejectedAsync(int limit, CancellationToken cancellationToken = default) =>
        reader.GetRejectedAsync(limit, cancellationToken);

    public Task<IReadOnlyList<T>> QueryPageAsync(string? afterId, int limit, bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        reader.QueryPageAsync(afterId, limit, includeDeleted, cancellationToken);

    public Task<int> PurgeAsync(IReadOnlyList<string> ids, long generation, CancellationToken cancellationToken = default) =>
        writer.PurgeAsync(ids, generation, cancellationToken);

    public Task<IReadOnlyList<T>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        reader.QueryAsync(includeDeleted, cancellationToken);

    public Task<ReplicaCursor> GetCursorAsync(CancellationToken cancellationToken = default) => reader.GetCursorAsync(cancellationToken);

    public Task<Clocks.HlcTimestamp> GetClockHighWaterAsync(CancellationToken cancellationToken = default) => reader.GetClockHighWaterAsync(cancellationToken);
}
