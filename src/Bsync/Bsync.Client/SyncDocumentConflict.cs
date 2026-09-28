namespace Bsync.Client;

/// <summary>A local change kept after it conflicted with a newer server change.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Id">The document id.</param>
/// <param name="Local">The local change that was not applied (a tombstone if it was a delete).</param>
/// <param name="Server">The server state the change conflicted with (a tombstone if the server deleted it). The server may have moved on since; <see cref="ISyncCollection{TDocument}.GetAsync"/> returns the newest known state.</param>
/// <param name="Base">The server state the local change was based on, if known; useful for three-way merges.</param>
public sealed record SyncDocumentConflict<TDocument>(string Id, TDocument Local, TDocument Server, TDocument? Base)
    where TDocument : class, ISyncEntity;
