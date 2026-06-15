namespace BlazorSync.Conflicts;

/// <summary>
/// The default strategy: the local (client) change always wins. The fork is kept and re-pushed so it
/// overwrites the concurrent server change. Simple and predictable, and a good fit for
/// single-user-multi-device apps where the user's most recent intent on a device should prevail.
/// </summary>
/// <remarks>
/// Because conflicts are re-pushed against the real master, the local change is never silently lost.
/// The trade-off is that a concurrent change made elsewhere can be overwritten; choose
/// <see cref="LastWriteWinsConflictHandler{TDocument}"/> or a custom merge when that matters.
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
/// Resolves by comparing Hybrid Logical Clock timestamps: the document with the greater
/// <see cref="ISyncEntity.UpdatedAt"/> wins. Because HLC ordering is a deterministic total order, all
/// peers reach the same result. Ties (which require identical node ids) fall back to the server.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class LastWriteWinsConflictHandler<TDocument> : IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <inheritdoc />
    public ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Fork.UpdatedAt > context.RealMaster.UpdatedAt
            ? ConflictResolution<TDocument>.Resolve(context.Fork)
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
