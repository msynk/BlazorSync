using Bsync.Protocol;
using Bsync.Transport;

namespace Bsync.Tests.TestSupport;

/// <summary>
/// Wraps a transport with deterministic network faults: lost responses after the server committed,
/// response rewriting, and hooks that run while a request is "in flight".
/// </summary>
public sealed class FaultyTransport<T>(ISyncTransport<T> inner) : ISyncTransport<T>
    where T : class, ISyncEntity
{
    private int _activePushes;

    public List<PushRequest<T>> PushLog { get; } = [];

    public int PullCalls { get; private set; }

    public int MaxConcurrentPushes { get; private set; }

    /// <summary>Number of upcoming pushes whose response is dropped after the server applied them.</summary>
    public int LoseResponses { get; set; }

    /// <summary>When set, the next push fails before reaching the server.</summary>
    public bool FailBeforeSend { get; set; }

    /// <summary>Runs after the server answered and before the engine sees the response (edit "during flight").</summary>
    public Func<PushResult<T>, Task<PushResult<T>>>? AfterPush { get; set; }

    /// <summary>Runs after the server answered a pull and before the engine sees the page.</summary>
    public Func<PullResult<T>, Task<PullResult<T>>>? AfterPull { get; set; }

    /// <summary>Runs before a push request is delivered (after the engine persisted its operations).</summary>
    public Func<PushRequest<T>, Task>? BeforePush { get; set; }

    public async Task<PullResult<T>> PullAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        PullCalls++;
        var result = await inner.PullAsync(request, cancellationToken);
        return AfterPull is { } hook ? await hook(result) : result;
    }

    public async Task<PushResult<T>> PushAsync(PushRequest<T> request, CancellationToken cancellationToken = default)
    {
        MaxConcurrentPushes = Math.Max(MaxConcurrentPushes, Interlocked.Increment(ref _activePushes));
        try
        {
            if (BeforePush is { } before)
            {
                await before(request);
            }

            if (FailBeforeSend)
            {
                FailBeforeSend = false;
                throw new InjectedFaultException("network unavailable");
            }

            PushLog.Add(request);
            var result = await inner.PushAsync(request, cancellationToken);
            if (LoseResponses > 0)
            {
                LoseResponses--;
                throw new InjectedFaultException("response lost after server commit");
            }

            return AfterPush is { } after ? await after(result) : result;
        }
        finally
        {
            Interlocked.Decrement(ref _activePushes);
        }
    }

    public IAsyncEnumerable<StreamEvent<T>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) =>
        inner.StreamAsync(since, cancellationToken);
}
