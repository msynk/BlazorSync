namespace Bsync.Conflicts;

/// <summary>
/// Resolves conflicts detected during push. Implementations run on the client.
/// </summary>
/// <remarks>
/// A handler must be deterministic for its inputs and free of external side effects: the engine may
/// call it again for the same document if the conflict recurs, and it must never be the only record
/// of user intent. Provide a custom implementation to merge fields or apply domain rules.
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Resolves a single conflicting document.</summary>
    ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context);
}
