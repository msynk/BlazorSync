namespace BlazorSync.Storage;

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

    /// <summary>Whether the record is waiting to be pushed (dirty and not rejected).</summary>
    public bool IsPushable => IsDirty && Rejection is null;

    /// <summary>The greatest server version known for this record.</summary>
    public long? KnownVersion => (BaseVersion, ObservedVersion) switch
    {
        ({ } b, { } o) => Math.Max(b, o),
        (var b, var o) => b ?? o,
    };
}

/// <summary>An immutable, persisted push operation.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="OperationId">The globally unique operation id sent to the server.</param>
/// <param name="Revision">The <see cref="SyncRecord{TDocument}.LocalRevision"/> the payload was taken from.</param>
/// <param name="BaseVersion">The server version the operation is based on.</param>
/// <param name="Payload">The exact document state sent to the server.</param>
public sealed record PendingOperation<TDocument>(string OperationId, long Revision, long? BaseVersion, TDocument Payload)
    where TDocument : class, ISyncEntity;

/// <summary>A permanent server rejection of one local revision.</summary>
/// <param name="Revision">The local revision that was rejected.</param>
/// <param name="ErrorCode">The server's machine-readable reason.</param>
/// <param name="Message">The server's explanation, if any.</param>
public sealed record SyncRejection(long Revision, string ErrorCode, string? Message);
