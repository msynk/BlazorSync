namespace Bsync.Conflicts;

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
