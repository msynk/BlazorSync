namespace Bsync.Conflicts;

/// <summary>
/// The server's current state always wins; local changes that conflict are discarded. Mirrors
/// RxDB's default and protects against a long-offline client clobbering newer collaborative changes.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class ServerWinsConflictHandler<TDocument> : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context) =>
        ConflictResolution<TDocument>.AcceptMaster();
}
