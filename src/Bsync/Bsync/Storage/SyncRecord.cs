namespace Bsync.Storage;

/// <summary>
/// A document together with the replication metadata the engine needs to track divergence between
/// the local state and the last confirmed server state.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Current">The app-visible state. What queries return and the user edits.</param>
/// <param name="Base">
/// The last server state this record was reconciled with (the "assumed master", used as the common
/// ancestor for three-way merges). <see langword="null"/> for a record created locally that the server
/// has never acknowledged.
/// </param>
/// <param name="IsDirty">
/// <see langword="true"/> when <paramref name="Current"/> contains local changes not yet confirmed by
/// the server. Dirty records are protected from being overwritten by pulls.
/// </param>
public sealed record SyncRecord<TDocument>(TDocument Current, TDocument? Base, bool IsDirty)
    where TDocument : class, ISyncEntity
{
    /// <summary>The server version of <see cref="Base"/>, or <see langword="null"/> if never confirmed.</summary>
    public long? BaseVersion { get; init; }

    /// <summary>
    /// Local revision of <see cref="Current"/>. Incremented by every local write; used to decide whether
    /// an acknowledgement still describes the latest local edit. Never sent to the server.
    /// </summary>
    public long LocalRevision { get; init; }

    /// <summary>
    /// The operation prepared from this record and possibly already sent. It is immutable once created
    /// and is resent unchanged until the server reports a final outcome, even if <see cref="Current"/>
    /// has since changed.
    /// </summary>
    public PendingOperation<TDocument>? Pending { get; init; }

    /// <summary>
    /// Set when the server permanently rejected the write of <see cref="SyncRejection.Revision"/>.
    /// A rejected record is not pushed again until a new local write replaces it.
    /// </summary>
    public SyncRejection? Rejection { get; init; }

    /// <summary>
    /// The newest server state a pull delivered while the record had unconfirmed local changes, when it
    /// is newer than <see cref="Base"/>. The pull checkpoint moves past it, so it is kept here and adopted
    /// if the local change later settles on an older version (for example when a retried push receives a
    /// replayed acknowledgement).
    /// </summary>
    public TDocument? Observed { get; init; }

    /// <summary>The server version of <see cref="Observed"/>.</summary>
    public long? ObservedVersion { get; init; }

    /// <summary>
    /// The replica generation (<see cref="ReplicaCursor.Generation"/>) in which the server state of this
    /// record was last confirmed. Versions are only comparable within one generation.
    /// </summary>
    public long Generation { get; init; }

    /// <summary>
    /// Set when a resnapshot after a reset completed without the server returning this record. The record
    /// is kept for inspection but hidden from queries; it is not a deletion and is not propagated. A later
    /// pull or local write of the same id clears it.
    /// </summary>
    public bool MissingAfterReset { get; init; }

    /// <summary>
    /// An unresolved conflict kept for the application or user (see <see cref="Conflicts.ConflictOutcome.Defer"/>):
    /// the record shows the server's state while the local change waits here. Cleared by resolving or discarding.
    /// </summary>
    public SyncConflict<TDocument>? Conflict { get; init; }

    /// <summary>
    /// The dependency group this record's unsynchronized change belongs to (<c>SyncEngine.WriteGroupAsync</c>): the
    /// group's changes are applied by the server all together or not at all. Cleared when the group is accepted.
    /// </summary>
    public SyncGroup? Group { get; init; }

    /// <summary>Whether the record is waiting to be pushed (dirty and not rejected).</summary>
    public bool IsPushable => IsDirty && Rejection is null;

    /// <summary>The greatest server version known for this record.</summary>
    public long? KnownVersion => (BaseVersion, ObservedVersion) switch
    {
        ({ } b, { } o) => Math.Max(b, o),
        (var b, var o) => b ?? o,
    };
}
