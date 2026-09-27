using System.Runtime.CompilerServices;
using BlazorSync.Protocol;
using BlazorSync.Transport;

namespace BlazorSync.Server;

/// <summary>
/// An <see cref="ISyncTransport{TDocument}"/> that calls an <see cref="InMemorySyncServer{TDocument}"/>
/// directly in-process, with no network. Useful for tests and samples.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InProcessTransport<TDocument> : ISyncTransport<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly InMemorySyncServer<TDocument> _server;

    /// <summary>Wraps the given in-memory server.</summary>
    public InProcessTransport(InMemorySyncServer<TDocument> server)
    {
        ArgumentNullException.ThrowIfNull(server);
        _server = server;
    }

    /// <inheritdoc />
    public Task<PullResult<TDocument>> PullAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_server.Pull(request));
    }

    /// <inheritdoc />
    public Task<PushResult<TDocument>> PushAsync(PushRequest<TDocument> request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_server.Push(request));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent<TDocument>> StreamAsync(
        Checkpoint since,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Live notifications are not implemented; callers use checkpoint pulls.
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("Live streaming is not implemented for the in-process transport.");
        #pragma warning disable CS0162 // Unreachable code: required to satisfy the iterator's yield contract.
        yield break;
        #pragma warning restore CS0162
    }
}
