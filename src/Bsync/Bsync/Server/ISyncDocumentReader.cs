using Bsync.Protocol;

namespace Bsync.Server;

/// <summary>
/// Authorized direct reads from an authority, for server-connected hosts (Interactive Server, prerendering,
/// static SSR) that show current server state without a local replica. Applies the same read authorization
/// as the change feed.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ISyncDocumentReader<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Returns the document if it exists and the caller may read it.</summary>
    Task<StoredDocument<TDocument>?> GetAsync(SyncCallContext context, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> readable, non-deleted documents in id order, starting after
    /// <paramref name="afterId"/> (keyset pagination).
    /// </summary>
    Task<IReadOnlyList<StoredDocument<TDocument>>> ListAsync(SyncCallContext context, int limit, string? afterId = null, CancellationToken cancellationToken = default);
}
