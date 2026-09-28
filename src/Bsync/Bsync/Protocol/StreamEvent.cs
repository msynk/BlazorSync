namespace Bsync.Protocol;

/// <summary>
/// An event from the server's live change stream. Live events are hints: missing any of them must
/// only delay synchronization until the next checkpoint pull, never lose data.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record StreamEvent<TDocument>
    where TDocument : class, ISyncEntity
{
    private StreamEvent(StreamEventKind kind, IReadOnlyList<RemoteChange<TDocument>> changes, Checkpoint checkpoint)
    {
        Kind = kind;
        Changes = changes;
        Checkpoint = checkpoint;
    }

    /// <summary>The event kind.</summary>
    public StreamEventKind Kind { get; }

    /// <summary>The changes (empty for a resync event).</summary>
    public IReadOnlyList<RemoteChange<TDocument>> Changes { get; }

    /// <summary>The checkpoint after applying <see cref="Changes"/> (ignored for a resync event).</summary>
    public Checkpoint Checkpoint { get; }

    /// <summary>Creates a changes event.</summary>
    public static StreamEvent<TDocument> ForChanges(IReadOnlyList<RemoteChange<TDocument>> changes, Checkpoint checkpoint) =>
        new(StreamEventKind.Changes, changes, checkpoint);

    /// <summary>Creates a resync signal event.</summary>
    public static StreamEvent<TDocument> Resync() =>
        new(StreamEventKind.Resync, Array.Empty<RemoteChange<TDocument>>(), Checkpoint.Start);
}
