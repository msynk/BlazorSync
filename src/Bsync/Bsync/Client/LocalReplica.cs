using Bsync.Conflicts;
using Bsync.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bsync.Client;

/// <summary>A local replica opened for one account: its store and the HLC node id to stamp writes with.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Store">The durable local store (disposed by the session if it implements <see cref="IAsyncDisposable"/>).</param>
/// <param name="NodeId">A node id unique to this replica incarnation (for example <see cref="ReplicaIdentity.Incarnation"/>).</param>
public sealed record LocalReplica<TDocument>(ILocalStore<TDocument> Store, string NodeId)
    where TDocument : class, ISyncEntity;
