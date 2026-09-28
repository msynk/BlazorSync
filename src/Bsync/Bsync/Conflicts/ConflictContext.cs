namespace Bsync.Conflicts;

/// <summary>
/// The three document states involved in a conflict, surfaced to an <see cref="IConflictHandler{TDocument}"/>
/// so it has everything needed to merge. All three are independent copies the handler may mutate.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="RealMaster">The server's current authoritative state.</param>
/// <param name="AssumedMaster">
/// The server state the local edit was based on (the common ancestor), or <see langword="null"/> if
/// the client thought the document was new. The difference between <paramref name="AssumedMaster"/>
/// and <paramref name="RealMaster"/> is precisely the concurrent change made elsewhere.
/// </param>
/// <param name="Fork">The latest local state, including edits made after the conflicting push was sent.</param>
public sealed record ConflictContext<TDocument>(
    TDocument RealMaster,
    TDocument? AssumedMaster,
    TDocument Fork)
    where TDocument : class, ISyncEntity;
