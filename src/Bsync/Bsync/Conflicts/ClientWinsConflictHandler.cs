namespace Bsync.Conflicts;

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
