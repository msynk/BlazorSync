namespace BlazorSync.Core.Protocol;

/// <summary>
/// Identifies the kind of <see cref="StreamEvent{TDocument}"/> emitted by the live pull stream.
/// </summary>
public enum StreamEventKind
{
    /// <summary>The event carries a batch of changed documents and a new checkpoint.</summary>
    Changes = 0,

    /// <summary>
    /// The server signals that an unknown amount of state has changed (for example after the client
    /// reconnects and may have missed events). The client must fall back to checkpoint iteration to
    /// catch up before resuming live observation.
    /// </summary>
    Resync = 1,
}

/// <summary>
/// An event from the server's live change stream, consumed during the "event observation" phase of
/// replication. Either a batch of changed documents or a <see cref="StreamEventKind.Resync"/>
/// signal.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record StreamEvent<TDocument>
    where TDocument : class, ISyncEntity
{
    private StreamEvent(StreamEventKind kind, IReadOnlyList<TDocument> documents, Checkpoint checkpoint)
    {
        Kind = kind;
        Documents = documents;
        Checkpoint = checkpoint;
    }

    /// <summary>The event kind.</summary>
    public StreamEventKind Kind { get; }

    /// <summary>The changed documents (empty for a resync event).</summary>
    public IReadOnlyList<TDocument> Documents { get; }

    /// <summary>The checkpoint after applying <see cref="Documents"/> (ignored for a resync event).</summary>
    public Checkpoint Checkpoint { get; }

    /// <summary>Creates a changes event.</summary>
    public static StreamEvent<TDocument> ForChanges(IReadOnlyList<TDocument> documents, Checkpoint checkpoint) =>
        new(StreamEventKind.Changes, documents, checkpoint);

    /// <summary>Creates a resync signal event.</summary>
    public static StreamEvent<TDocument> Resync() =>
        new(StreamEventKind.Resync, Array.Empty<TDocument>(), Checkpoint.Start);
}
