namespace BlazorSync.Core.Conflicts;

/// <summary>
/// The three document states involved in a conflict, surfaced to an <see cref="IConflictHandler{TDocument}"/>
/// so it has everything needed to merge.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="RealMaster">The server's current authoritative state.</param>
/// <param name="AssumedMaster">
/// The state the client believed was current when it made the local write, or
/// <see langword="null"/> if the client thought the document was new. The difference between
/// <paramref name="AssumedMaster"/> and <paramref name="RealMaster"/> is precisely the concurrent
/// change made elsewhere.
/// </param>
/// <param name="Fork">The client's local (losing or winning, depending on strategy) state.</param>
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

    /// <summary>Write a resolved document, which the engine will re-push to the server.</summary>
    UseResolved = 1,
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

    /// <summary>Persist and re-push <paramref name="resolved"/> as the new authoritative state.</summary>
    public static ConflictResolution<TDocument> Resolve(TDocument resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return new ConflictResolution<TDocument>(ConflictOutcome.UseResolved, resolved);
    }
}

/// <summary>
/// Resolves conflicts detected during push. Implementations run entirely on the client, keeping the
/// server logic minimal. Provide a custom implementation to merge fields, prompt the user, or apply
/// domain-specific rules.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface IConflictHandler<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Resolves a single conflicting document.</summary>
    ConflictResolution<TDocument> Resolve(ConflictContext<TDocument> context);
}
