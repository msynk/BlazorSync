using BlazorSync.Core.Protocol;

namespace BlazorSync.Core.Transport;

/// <summary>
/// The client-side view of the server for a single collection. Implementations carry the protocol
/// messages over a wire (HTTP for pull/push, SignalR or SSE for the live stream) or, in tests, call
/// an in-process server directly. The engine depends only on this abstraction.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public interface ISyncTransport<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Pulls the next batch of server changes after the request's checkpoint.</summary>
    Task<PullResult<TDocument>> PullAsync(PullRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pushes a batch of local writes and returns any conflicts.</summary>
    Task<PushResult<TDocument>> PushAsync(PushRequest<TDocument> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to the server's live change stream for event-observation mode (Phase 3). The
    /// stream emits batches of changes and periodic <see cref="StreamEventKind.Resync"/> signals.
    /// Implementations that do not support live streaming may throw
    /// <see cref="NotSupportedException"/>; the engine falls back to checkpoint iteration.
    /// </summary>
    IAsyncEnumerable<StreamEvent<TDocument>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default);
}
