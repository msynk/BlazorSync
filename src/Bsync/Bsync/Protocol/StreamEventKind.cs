namespace Bsync.Protocol;

/// <summary>
/// Identifies the kind of <see cref="StreamEvent{TDocument}"/> emitted by the live pull stream.
/// </summary>
public enum StreamEventKind
{
    /// <summary>The event carries a batch of changes and a new checkpoint.</summary>
    Changes = 0,

    /// <summary>
    /// The server signals that an unknown amount of state has changed (for example after the client
    /// reconnects and may have missed events). The client must fall back to checkpoint iteration to
    /// catch up before resuming live observation.
    /// </summary>
    Resync = 1,
}
