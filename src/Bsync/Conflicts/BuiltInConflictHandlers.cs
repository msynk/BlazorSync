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

/// <summary>
/// The local (client) change always wins. The fork is kept and re-pushed so it
/// overwrites the concurrent server change. Simple and predictable, and a good fit for
/// single-user-multi-device apps where the user's most recent intent on a device should prevail.
/// </summary>
/// <remarks>
/// Because conflicts are re-pushed against the real master, the local change is never silently lost.
/// The trade-off is that a concurrent change made elsewhere is overwritten (a lossy policy), and the
/// final state depends on which replica uploads last. The resolved state is treated as a new local edit
/// and re-stamped. Choose <see cref="LastWriteWinsConflictHandler{TDocument}"/> for an upload-order
/// independent result, or a custom three-way merge when neither edit may be lost.
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class ClientWinsConflictHandler<TDocument> : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ConflictResolution<TDocument>.Resolve(context.Fork);
    }
}

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

/// <summary>
/// Resolves by comparing authoring timestamps: the state with the greater origin
/// <see cref="ISyncEntity.UpdatedAt"/> wins, whole-document, including deletions. A winning local
/// state is re-pushed with its original timestamp (<see cref="ConflictResolution{TDocument}.KeepFork"/>),
/// so the result does not depend on which replica uploads first. Ties (which require identical node
/// ids) fall back to the server.
/// </summary>
/// <remarks>
/// This is a lossy policy: the losing concurrent edit is discarded. Its correctness depends on replica
/// clocks being roughly synchronized; the server bounds forward skew but cannot detect a clock that runs
/// behind.
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class LastWriteWinsConflictHandler<TDocument> : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Fork.UpdatedAt > context.RealMaster.UpdatedAt
            ? ConflictResolution<TDocument>.KeepFork()
            : ConflictResolution<TDocument>.AcceptMaster();
    }
}

/// <summary>
/// Adapts a delegate into an <see cref="IConflictHandler{TDocument}"/>, the simplest way to plug in
/// custom merge logic without declaring a class.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class DelegateConflictHandler<TDocument> : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly Func<ConflictContext<TDocument>, ConflictResolution<TDocument>> _resolve;

    /// <summary>Wraps <paramref name="resolve"/> as a conflict handler.</summary>
    public DelegateConflictHandler(Func<ConflictContext<TDocument>, ConflictResolution<TDocument>> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
    }

    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context) => _resolve(context);
}
