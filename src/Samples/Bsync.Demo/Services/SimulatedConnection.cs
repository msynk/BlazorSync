using Bsync.Protocol;
using Bsync.Transport;
using Bsync.Demo.Models;

namespace Bsync.Demo.Services;

/// <summary>
/// Decorates a real <see cref="ISyncTransport{TDocument}"/> with a simulated network: an
/// <see cref="IsOnline"/> toggle (offline throws <see cref="OfflineException"/>) and an optional
/// artificial latency so syncs feel realistic in the UI. This is the seam the demo uses to show
/// that local reads/writes keep working while offline and that queued changes flush on reconnect.
/// </summary>
public sealed class SimulatedConnection : ISyncTransport<DemoNote>
{
    private readonly ISyncTransport<DemoNote> _inner;

    /// <summary>Creates the connection over an underlying transport.</summary>
    public SimulatedConnection(ISyncTransport<DemoNote> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Whether the device can currently reach the server.</summary>
    public bool IsOnline { get; set; } = true;

    /// <summary>Artificial round-trip latency applied to each call.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public async Task<PullResult<DemoNote>> PullAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        await GateAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.PullAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PushResult<DemoNote>> PushAsync(PushRequest<DemoNote> request, CancellationToken cancellationToken = default)
    {
        await GateAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.PushAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<StreamEvent<DemoNote>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) =>
        _inner.StreamAsync(since, cancellationToken);

    private async Task GateAsync(CancellationToken cancellationToken)
    {
        if (!IsOnline)
        {
            throw new OfflineException();
        }

        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, cancellationToken).ConfigureAwait(false);
        }
    }
}
