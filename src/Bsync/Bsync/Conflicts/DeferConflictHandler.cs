namespace Bsync.Conflicts;

/// <summary>
/// The default strategy: nothing is overwritten and nothing is lost. The replica shows the server's state and keeps
/// the local change as an unresolved conflict, with the common ancestor, until the application or user resolves it
/// (<c>SyncEngine.ResolveConflictAsync</c>) or discards it.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class DeferConflictHandler<TDocument> : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context) => ConflictResolution<TDocument>.Defer();
}
