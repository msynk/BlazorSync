using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Storage;
using Bsync.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bsync.Client;

/// <summary>A local replica opened for one account: its store and the HLC node id to stamp writes with.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Store">The durable local store (disposed by the session if it implements <see cref="IAsyncDisposable"/>).</param>
/// <param name="NodeId">A node id unique to this replica incarnation (for example <see cref="ReplicaIdentity.Incarnation"/>).</param>
public sealed record LocalReplica<TDocument>(ILocalStore<TDocument> Store, string NodeId)
    where TDocument : class, ISyncEntity;

/// <summary>Configuration of a <see cref="SyncSession{TDocument}"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed record SyncSessionOptions<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Opens the replica for an account. Each account must get its own storage.</summary>
    public required Func<string, CancellationToken, Task<LocalReplica<TDocument>>> OpenReplica { get; init; }

    /// <summary>Creates the transport used for an account (its credentials belong to that account).</summary>
    public required Func<string, ISyncTransport<TDocument>> CreateTransport { get; init; }

    /// <summary>Deep-clone function for documents (trim/AOT safe, for example <c>DocumentCloner.Json</c>).</summary>
    public required Func<TDocument, TDocument> Cloner { get; init; }

    /// <summary>
    /// Takes the replication lease for an account, or returns <see langword="null"/> if another instance (tab)
    /// holds it. When not set, this session always replicates.
    /// </summary>
    public Func<string, CancellationToken, Task<IAsyncDisposable?>>? AcquireLease { get; init; }

    /// <summary>Conflict policy. Default: the engine's default.</summary>
    public IConflictHandler<TDocument>? ConflictHandler { get; init; }

    /// <summary>Engine batch sizes and budgets.</summary>
    public SyncOptions<TDocument>? EngineOptions { get; init; }

    /// <summary>A short host description reported in <see cref="SyncCapabilities.Host"/>. Default <c>local</c>.</summary>
    public string Host { get; init; } = "local";

    /// <summary>Time between syncs when idle. Default 30 seconds.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>First retry delay after a transient failure. Default 1 second.</summary>
    public TimeSpan MinBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest retry delay. Default 5 minutes.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How often a follower (no lease) announces possible changes made by the owner. Default 2 seconds.</summary>
    public TimeSpan FollowerRefresh { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Time source for delays (tests use a fake). Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Listen to the transport's hint stream (<see cref="ISyncTransport{TDocument}.StreamAsync"/>) and sync as soon
    /// as the server announces a change. Hints only shorten the wait: a missed hint delays synchronization until
    /// the next interval, never loses data (I13). Default <see langword="false"/>.
    /// </summary>
    public bool LiveHints { get; init; }

    /// <summary>
    /// Called when a replica opens; returns something to dispose when it closes. Hosts use it to connect platform
    /// events (browser <c>online</c>/visibility, native resume) to <see cref="SyncSession{TDocument}.RequestSync"/>.
    /// </summary>
    public Func<SyncSession<TDocument>, string, CancellationToken, Task<IAsyncDisposable?>>? AttachLifecycle { get; init; }

    /// <summary>
    /// Called when the server answers <c>unauthorized</c>. Return <see langword="true"/> after renewing credentials
    /// to retry at once; otherwise the session reports <see cref="SyncState.AttentionRequired"/>.
    /// </summary>
    public Func<string, CancellationToken, Task<bool>>? RenewCredentials { get; init; }

    /// <summary>
    /// Receives state changes and failures (category <c>Bsync.SyncSession</c>). Messages carry states, counts and
    /// error codes, never document data or account names. The DI recipes use the container's logger factory.
    /// </summary>
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Owns one local replica and its replication loop: opens the replica for the current account, syncs when
/// asked, on an interval and after local writes, backs off with jitter on transient failures (honouring
/// <c>Retry-After</c>), yields to the tab that holds the lease, and reports <see cref="Status"/>.
/// </summary>
/// <remarks>
/// <para>
/// Switching account (<see cref="GetEngineAsync"/> with a different account, or <see cref="StopAsync"/>) stops
/// the loop and waits for it before the next account's replica opens, so a response started for the previous
/// account is never applied to the new one. Each account's data stays in its own storage.
/// </para>
/// <para>
/// Register one session per app instance (a WebAssembly tab, a native app). Never share one across users on a
/// server.
/// </para>
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class SyncSession<TDocument> : IAsyncDisposable
    where TDocument : class, ISyncEntity
{
    private readonly SyncSessionOptions<TDocument> _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _trigger = new(0, 1);
    private Active? _active;
    private SyncStatus _status = SyncStatus.Starting;
    private volatile bool _paused;
    private int _disposed;

    /// <summary>Creates a session. Nothing is opened until first use.</summary>
    public SyncSession(SyncSessionOptions<TDocument> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Interval, TimeSpan.Zero, nameof(options.Interval));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MinBackoff, TimeSpan.Zero, nameof(options.MinBackoff));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxBackoff, options.MinBackoff, nameof(options.MaxBackoff));
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
    }

    /// <summary>Raised when documents or <see cref="Status"/> may have changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>The current status.</summary>
    public SyncStatus Status => _status;

    /// <summary>The account whose replica is open, if any.</summary>
    public string? Account => _active?.Account;

    /// <summary>The host description from the options.</summary>
    public string Host => _options.Host;

    /// <summary>Whether the session listens to server hints.</summary>
    public bool LiveHints => _options.LiveHints;

    /// <summary>
    /// Returns the engine for <paramref name="account"/>, opening its replica and starting replication if needed.
    /// If another account's replica is open, it is stopped first.
    /// </summary>
    public async Task<SyncEngine<TDocument>> GetEngineAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_active is { } current && current.Account == account)
        {
            return current.Engine;
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active is { } active)
            {
                if (active.Account == account)
                {
                    return active.Engine;
                }

                await StopActiveAsync().ConfigureAwait(false);
            }

            var replica = await _options.OpenReplica(account, cancellationToken).ConfigureAwait(false);
            var transport = _options.CreateTransport(account);
            var engine = new SyncEngine<TDocument>(
                replica.Store,
                transport,
                new HybridLogicalClock(replica.NodeId),
                _options.Cloner,
                _options.ConflictHandler,
                _options.EngineOptions);
            var stopping = new CancellationTokenSource();
            var subscription = engine.Observe(_ => Changed?.Invoke());
            var started = new Active(account, replica, engine, transport, stopping, subscription);
            _active = started;
            SetStatus(SyncStatus.Starting);
            started.Loop = Task.Run(() => RunAsync(started, stopping.Token), CancellationToken.None);
            if (_options.LiveHints)
            {
                started.Hints = Task.Run(() => ListenAsync(started, stopping.Token), CancellationToken.None);
            }

            if (_options.AttachLifecycle is { } attach)
            {
                started.Lifecycle = await attach(this, account, cancellationToken).ConfigureAwait(false);
            }

            return engine;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Records that a local write committed: updates <see cref="SyncStatus.Pending"/> at once (so the UI never shows
    /// a stale "nothing pending") and asks the loop to sync.
    /// </summary>
    public async Task NotifyLocalWriteAsync(CancellationToken cancellationToken = default)
    {
        if (_active is { } active)
        {
            var pending = await active.Engine.CountDirtyAsync(cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(_active, active))
            {
                SetStatus(_status with { Pending = pending });
            }
        }

        RequestSync();
    }

    /// <summary>Asks the loop to sync soon (for example after a local write or when the network returns).</summary>
    public void RequestSync()
    {
        try
        {
            _trigger.Release();
        }
        catch (SemaphoreFullException)
        {
            // A sync is already requested.
        }
    }

    /// <summary>
    /// Pauses replication after the current sync (for example when a native app is suspended). Local reads and
    /// writes keep working. Call <see cref="Resume"/> to continue.
    /// </summary>
    public void Pause() => _paused = true;

    /// <summary>Resumes replication and syncs at once (for example when a native app returns to the foreground).</summary>
    public void Resume()
    {
        _paused = false;
        RequestSync();
    }

    /// <summary>Stops replication and closes the current replica (for example on sign-out).</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopActiveAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The delay before retry number <paramref name="failures"/>: exponential, capped, with jitter, never shorter than <paramref name="retryAfter"/>.</summary>
    internal TimeSpan Backoff(int failures, TimeSpan? retryAfter)
    {
        var exponential = _options.MinBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 30));
        var capped = Math.Min(exponential, _options.MaxBackoff.TotalMilliseconds);
        var jittered = TimeSpan.FromMilliseconds(capped * (0.5 + (Random.Shared.NextDouble() / 2)));
        return retryAfter is { } server && server > jittered ? server : jittered;
    }

    private async Task StopActiveAsync()
    {
        if (_active is not { } active)
        {
            return;
        }

        _active = null;
        await active.Stopping.CancelAsync().ConfigureAwait(false);
        if (active.Loop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (active.Hints is { } hints)
        {
            try
            {
                await hints.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (active.Lifecycle is { } lifecycle)
        {
            await lifecycle.DisposeAsync().ConfigureAwait(false);
        }

        active.Subscription.Dispose();
        if (active.Lease is { } lease)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }

        if (active.Replica.Store is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }

        active.Stopping.Dispose();
        SetStatus(new SyncStatus(SyncState.Stopped, 0, null, _status.LastSynced));
    }

    private async Task RunAsync(Active active, CancellationToken stopping)
    {
        var failures = 0;
        var renewed = false;
        while (!stopping.IsCancellationRequested)
        {
            if (_paused)
            {
                await PublishAsync(active, SyncState.Paused, null).ConfigureAwait(false);
                try
                {
                    await WaitAsync(Timeout.InfiniteTimeSpan, honourTriggers: true, stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            TimeSpan wait;
            var honourTriggers = true;
            try
            {
                if (_options.AcquireLease is { } acquire && active.Lease is null)
                {
                    active.Lease = await acquire(active.Account, stopping).ConfigureAwait(false);
                }

                if (_options.AcquireLease is not null && active.Lease is null)
                {
                    await PublishAsync(active, SyncState.Follower, "Another instance syncs this replica.").ConfigureAwait(false);
                    wait = _options.FollowerRefresh;
                }
                else
                {
                    await PublishAsync(active, SyncState.Syncing, null).ConfigureAwait(false);
                    var result = await active.Engine.SyncAsync(stopping).ConfigureAwait(false);
                    failures = 0;
                    renewed = false;
                    if (result.Rejected > 0)
                    {
                        await PublishAsync(active, SyncState.AttentionRequired, $"{result.Rejected} change(s) were rejected by the server.").ConfigureAwait(false);
                    }
                    else if (result.IsComplete)
                    {
                        _status = _status with { LastSynced = _options.TimeProvider.GetUtcNow() };
                        await PublishAsync(active, SyncState.Synced, null).ConfigureAwait(false);
                    }
                    else
                    {
                        await PublishAsync(active, SyncState.Syncing, "More work is queued.").ConfigureAwait(false);
                    }

                    wait = result.HasRemainingWork ? TimeSpan.Zero : _options.Interval;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (SyncTransportException error) when (error.IsTransient)
            {
                failures++;
                wait = Backoff(failures, error.RetryAfter);
                honourTriggers = error.RetryAfter is null; // the server asked us to wait
                await PublishAsync(active, SyncState.Offline, "The server is unreachable; changes are kept on this device.").ConfigureAwait(false);
            }
            catch (SyncTransportException error) when (
                error.ErrorCode == SyncErrorCodes.Unauthorized && !renewed && _options.RenewCredentials is { } renew)
            {
                // One renewal attempt per failure streak; a second unauthorized answer needs the user.
                renewed = true;
                wait = await renew(active.Account, stopping).ConfigureAwait(false) ? TimeSpan.Zero : _options.MaxBackoff;
                if (wait > TimeSpan.Zero)
                {
                    await PublishAsync(active, SyncState.AttentionRequired, "Sign in again to continue syncing.").ConfigureAwait(false);
                }
            }
            catch (SyncTransportException error)
            {
                wait = _options.MaxBackoff;
                await PublishAsync(active, SyncState.AttentionRequired, $"Sync stopped: {error.ErrorCode}.").ConfigureAwait(false);
            }
            catch (LocalStoreUnavailableException error)
            {
                wait = _options.MaxBackoff;
                await PublishAsync(active, SyncState.AttentionRequired, $"Local storage problem: {error.Reason}.").ConfigureAwait(false);
            }
            catch (SyncProtocolException error)
            {
                SessionLog.ProtocolError(_logger, _options.Host, error);
                wait = _options.MaxBackoff;
                await PublishAsync(active, SyncState.AttentionRequired, "The server sent an invalid response.").ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // Anything else (a store or serializer failure, a bug) must not end the loop silently: report it, keep
                // local work, and try again later or when asked.
                SessionLog.UnexpectedError(_logger, _options.Host, error);
                wait = _options.MaxBackoff;
                await PublishAsync(active, SyncState.AttentionRequired, $"Sync stopped by an unexpected error ({error.GetType().Name}).").ConfigureAwait(false);
            }

            try
            {
                await WaitAsync(wait, honourTriggers, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Turns server hints into sync requests; reconnects with backoff; stops if the transport has no hints.</summary>
    private async Task ListenAsync(Active active, CancellationToken stopping)
    {
        var failures = 0;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await foreach (var _ in active.Transport.StreamAsync(Checkpoint.Start, stopping).ConfigureAwait(false))
                {
                    failures = 0;
                    RequestSync();
                }

                failures++;
            }
            catch (NotSupportedException)
            {
                return;
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Network or server trouble: the sync loop reports it; hints just reconnect later.
                failures++;
            }

            try
            {
                await Task.Delay(Backoff(Math.Max(failures, 1), null), _options.TimeProvider, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task WaitAsync(TimeSpan wait, bool honourTriggers, CancellationToken stopping)
    {
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        if (!honourTriggers)
        {
            await Task.Delay(wait, _options.TimeProvider, stopping).ConfigureAwait(false);
            return;
        }

        // Cancel whichever wait loses, so no orphaned trigger waiter can swallow a later RequestSync.
        using var round = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var delay = Task.Delay(wait, _options.TimeProvider, round.Token);
        var triggered = _trigger.WaitAsync(round.Token);
        await Task.WhenAny(delay, triggered).ConfigureAwait(false);
        await round.CancelAsync().ConfigureAwait(false);
        stopping.ThrowIfCancellationRequested();
    }

    private async Task PublishAsync(Active active, SyncState state, string? detail)
    {
        var pending = 0;
        try
        {
            pending = await active.Engine.CountDirtyAsync().ConfigureAwait(false);
        }
        catch (LocalStoreUnavailableException)
        {
        }

        if (ReferenceEquals(_active, active))
        {
            SetStatus(new SyncStatus(state, pending, detail, _status.LastSynced));
        }
    }

    private void SetStatus(SyncStatus status)
    {
        var previous = _status;
        _status = status;
        if (previous.State != status.State)
        {
            var level = status.State is SyncState.Offline or SyncState.AttentionRequired ? LogLevel.Warning
                : status.State is SyncState.Syncing or SyncState.Synced ? LogLevel.Debug
                : LogLevel.Information;
            SessionLog.StateChanged(_logger, level, _options.Host, previous.State, status.State, status.Pending, status.Detail);
        }

        Changed?.Invoke();
    }

    private sealed class Active(string account, LocalReplica<TDocument> replica, SyncEngine<TDocument> engine, ISyncTransport<TDocument> transport, CancellationTokenSource stopping, IDisposable subscription)
    {
        public ISyncTransport<TDocument> Transport { get; } = transport;

        public Task? Hints { get; set; }

        public IAsyncDisposable? Lifecycle { get; set; }

        public string Account { get; } = account;

        public LocalReplica<TDocument> Replica { get; } = replica;

        public SyncEngine<TDocument> Engine { get; } = engine;

        public CancellationTokenSource Stopping { get; } = stopping;

        public IDisposable Subscription { get; } = subscription;

        public Task? Loop { get; set; }

        public IAsyncDisposable? Lease { get; set; }
    }
}

/// <summary>Structured, allocation-free log messages for <see cref="SyncSession{TDocument}"/>.</summary>
internal static partial class SessionLog
{
    [LoggerMessage(EventId = 1, EventName = "SyncStateChanged", Message = "Sync ({Host}) {Previous} -> {State}; {Pending} pending. {Detail}")]
    public static partial void StateChanged(ILogger logger, LogLevel level, string host, SyncState previous, SyncState state, int pending, string? detail);

    [LoggerMessage(EventId = 2, EventName = "SyncProtocolError", Level = LogLevel.Error, Message = "Sync ({Host}) stopped: the server sent an invalid response.")]
    public static partial void ProtocolError(ILogger logger, string host, Exception error);

    [LoggerMessage(EventId = 3, EventName = "SyncUnexpectedError", Level = LogLevel.Error, Message = "Sync ({Host}) stopped by an unexpected error.")]
    public static partial void UnexpectedError(ILogger logger, string host, Exception error);
}
