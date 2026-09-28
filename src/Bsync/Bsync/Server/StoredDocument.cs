using Bsync.Protocol;

namespace Bsync.Server;

/// <summary>A document as currently stored by an authority, with its version.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Document">The current state (tombstones included only when asked for).</param>
/// <param name="Version">The server version, for use as the base of a later write.</param>
public sealed record StoredDocument<TDocument>(TDocument Document, long Version)
    where TDocument : class, ISyncEntity;
