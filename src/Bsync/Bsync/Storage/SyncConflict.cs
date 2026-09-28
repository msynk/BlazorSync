namespace Bsync.Storage;

/// <summary>A conflict kept for later resolution.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Server">The server state the local change conflicted with.</param>
/// <param name="ServerVersion">The server version of <paramref name="Server"/>.</param>
/// <param name="Local">The local change that was not applied.</param>
/// <param name="Base">The common ancestor the local change was made from, if known.</param>
public sealed record SyncConflict<TDocument>(TDocument Server, long ServerVersion, TDocument Local, TDocument? Base)
    where TDocument : class, ISyncEntity;
