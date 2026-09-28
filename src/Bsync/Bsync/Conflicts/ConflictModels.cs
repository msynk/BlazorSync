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

/// <summary>The decision a conflict handler reaches for a single conflicting document.</summary>
public enum ConflictOutcome
{
    /// <summary>Discard the local change and accept the server's current state.</summary>
    UseMaster = 0,

    /// <summary>
    /// Write a new resolved document. It is a new local edit: the engine stamps it with a fresh
    /// timestamp and re-pushes it based on the server's current version.
    /// </summary>
    UseResolved = 1,

    /// <summary>
    /// Keep the local state unchanged, including its original authoring timestamp, and re-push it
    /// based on the server's current version.
    /// </summary>
    KeepFork = 2,

    /// <summary>
    /// Adopt the server's state for now and keep the local change as an unresolved conflict
    /// (<c>SyncRecord.Conflict</c>) for the application or user to resolve later. Nothing is lost and nothing is
    /// pushed until then.
    /// </summary>
    Defer = 3,
}

/// <summary>The outcome of resolving a single conflict.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record ConflictResolution<TDocument>
    where TDocument : class, ISyncEntity
{
    private ConflictResolution(ConflictOutcome outcome, TDocument? resolved)
    {
        Outcome = outcome;
        Resolved = resolved;
    }

    /// <summary>The chosen outcome.</summary>
    public ConflictOutcome Outcome { get; }

    /// <summary>
    /// The resolved document to persist and re-push when <see cref="Outcome"/> is
    /// <see cref="ConflictOutcome.UseResolved"/>; otherwise <see langword="null"/>.
    /// </summary>
    public TDocument? Resolved { get; }

    /// <summary>Discard the local change and keep the server's state.</summary>
    public static ConflictResolution<TDocument> AcceptMaster() => new(ConflictOutcome.UseMaster, null);

    /// <summary>Show the server's state and keep the local change as an unresolved conflict.</summary>
    public static ConflictResolution<TDocument> Defer() => new(ConflictOutcome.Defer, null);

    /// <summary>Keep the local state (and its authoring timestamp) and re-push it.</summary>
    public static ConflictResolution<TDocument> KeepFork() => new(ConflictOutcome.KeepFork, null);

    /// <summary>Persist and re-push <paramref name="resolved"/> as a new local edit.</summary>
    public static ConflictResolution<TDocument> Resolve(TDocument resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return new ConflictResolution<TDocument>(ConflictOutcome.UseResolved, resolved);
    }
}

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
