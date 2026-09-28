using Bsync.Storage;
using Microsoft.JSInterop;

namespace Bsync.Blazor.IndexedDb;

/// <summary>
/// Exclusive ownership of replication for one replica across browser tabs, using the Web Locks API. Only
/// the holder should run the sync loop; other tabs keep reading and writing locally (the store's
/// optimistic commits keep them safe) and observe the owner's changes on their next read.
/// </summary>
/// <remarks>
/// The browser releases the lock when the holder disposes the lease or its tab closes or crashes, so a
/// stale owner cannot keep it. Ownership is an efficiency measure; correctness does not depend on it.
/// </remarks>
public sealed class IndexedDbReplicaLease : IAsyncDisposable
{
    private readonly IJSObjectReference _module;
    private readonly int _id;
    private int _released;

    private IndexedDbReplicaLease(IJSObjectReference module, int id)
    {
        _module = module;
        _id = id;
    }

    /// <summary>Tries to take the lease named <paramref name="name"/>; returns <see langword="null"/> if another tab holds it.</summary>
    /// <exception cref="LocalStoreUnavailableException">Web Locks are unavailable.</exception>
    public static async Task<IndexedDbReplicaLease?> TryAcquireAsync(IJSRuntime js, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var module = await IndexedDbLocalStore<Placeholder>.ImportAsync(js, cancellationToken).ConfigureAwait(false);
        var id = await IndexedDbLocalStore<Placeholder>.Call(() => module.InvokeAsync<int>("tryAcquireLease", cancellationToken, name)).ConfigureAwait(false);
        return id == 0 ? null : new IndexedDbReplicaLease(module, id);
    }

    /// <summary>Releases the lease so another tab can take it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            try
            {
                await _module.InvokeVoidAsync("releaseLease", _id).ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // The tab is closing; the browser releases the lock.
            }
        }
    }

    private sealed class Placeholder : ISyncEntity
    {
        public string Id { get; set; } = string.Empty;

        public Clocks.HlcTimestamp UpdatedAt { get; set; }

        public bool Deleted { get; set; }
    }
}
