using Bsync.Protocol;
using Bsync.Transport;

namespace Bsync.Tests.TestSupport;

/// <summary>Routes calls to whatever server an <see cref="InMemorySyncServerRef"/> currently holds.</summary>
public sealed class ServerRefTransport(InMemorySyncServerRef server) : ISyncTransport<Note>
{
    public Task<PullResult<Note>> PullAsync(PullRequest request, CancellationToken cancellationToken = default) =>
        new Server.InProcessTransport<Note>(server.Server).PullAsync(request, cancellationToken);

    public Task<PushResult<Note>> PushAsync(PushRequest<Note> request, CancellationToken cancellationToken = default) =>
        new Server.InProcessTransport<Note>(server.Server).PushAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamEvent<Note>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) =>
        new Server.InProcessTransport<Note>(server.Server).StreamAsync(since, cancellationToken);
}
