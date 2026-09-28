namespace Bsync.Conflicts;

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
