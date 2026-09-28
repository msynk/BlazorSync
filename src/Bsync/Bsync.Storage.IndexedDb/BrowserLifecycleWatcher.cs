using Microsoft.JSInterop;

namespace Bsync.Storage.IndexedDb;

/// <summary>
/// Calls a callback when the browser reports that the network is back (<c>online</c>) or the tab became visible
/// again. Browsers throttle or freeze timers in hidden tabs, so these events are when a sync is most useful.
/// </summary>
public sealed class BrowserLifecycleWatcher : IAsyncDisposable
{
    private readonly Action _wake;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserLifecycleWatcher>? _self;
    private int _id;

    private BrowserLifecycleWatcher(Action wake) => _wake = wake;

    /// <summary>Starts watching; dispose to stop.</summary>
    public static async Task<BrowserLifecycleWatcher> StartAsync(IJSRuntime js, Action wake, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentNullException.ThrowIfNull(wake);
        var watcher = new BrowserLifecycleWatcher(wake);
        watcher._module = await IndexedDbLocalStore<Placeholder>.ImportAsync(js, cancellationToken).ConfigureAwait(false);
        watcher._self = DotNetObjectReference.Create(watcher);
        watcher._id = await watcher._module.InvokeAsync<int>("watchLifecycle", cancellationToken, watcher._self).ConfigureAwait(false);
        return watcher;
    }

    /// <summary>Called from JavaScript.</summary>
    [JSInvokable]
    public void Wake() => _wake();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is { } module)
        {
            _module = null;
            try
            {
                await module.InvokeVoidAsync("unwatchLifecycle", _id).ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _self?.Dispose();
    }

    private sealed class Placeholder : ISyncEntity
    {
        public string Id { get; set; } = string.Empty;

        public Clocks.HlcTimestamp UpdatedAt { get; set; }

        public bool Deleted { get; set; }
    }
}
