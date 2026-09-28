namespace Bsync.Conflicts;

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
