using BlazorSync.Clocks;
using BlazorSync.Protocol;
using BlazorSync.Storage;
using BlazorSync.Transport;

namespace BlazorSync.Tests.TestSupport;

/// <summary>Thrown by fault-injecting doubles to simulate a crash or I/O failure.</summary>
public sealed class InjectedFaultException(string message) : IOException(message);

/// <summary>
/// Wraps a store and runs a hook before each <see cref="UpdateAsync"/> is committed. The hook can
/// interleave a concurrent local edit (to model a race between a read and a commit) or throw to model a
/// crash at a commit boundary.
/// </summary>
public sealed class InterceptingStore<T>(ILocalStore<T> inner) : ILocalStore<T>
    where T : class, ISyncEntity
{
    public ILocalStore<T> Inner { get; } = inner;

    public int UpdateCalls { get; private set; }

    /// <summary>Receives the 1-based call number, the updates and the cursor.</summary>
    public Func<int, IReadOnlyList<RecordUpdate<T>>, ReplicaCursor?, Task>? BeforeUpdate { get; set; }

    public async Task<IReadOnlyList<RecordUpdateResult<T>>> UpdateAsync(
        IReadOnlyList<RecordUpdate<T>> updates,
        ReplicaCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var call = ++UpdateCalls;
        if (BeforeUpdate is { } hook)
        {
            await hook(call, updates, cursor);
        }

        return await Inner.UpdateAsync(updates, cursor, cancellationToken);
    }

    public Task<SyncRecord<T>?> GetAsync(string id, CancellationToken cancellationToken = default) => Inner.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetPendingAsync(int limit, IReadOnlySet<string>? exclude = null, CancellationToken cancellationToken = default) =>
        Inner.GetPendingAsync(limit, exclude, cancellationToken);

    public Task<int> CountDirtyAsync(CancellationToken cancellationToken = default) => Inner.CountDirtyAsync(cancellationToken);

    public Task<IReadOnlyList<T>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        Inner.QueryAsync(includeDeleted, cancellationToken);

    public Task<ReplicaCursor> GetCursorAsync(CancellationToken cancellationToken = default) => Inner.GetCursorAsync(cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetStaleAsync(long generation, int limit, CancellationToken cancellationToken = default) =>
        Inner.GetStaleAsync(generation, limit, cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetConflictsAsync(int limit, CancellationToken cancellationToken = default) =>
        Inner.GetConflictsAsync(limit, cancellationToken);

    public Task<IReadOnlyList<SyncRecord<T>>> GetRejectedAsync(int limit, CancellationToken cancellationToken = default) =>
        Inner.GetRejectedAsync(limit, cancellationToken);

    public Task<int> PurgeAsync(IReadOnlyList<string> ids, long generation, CancellationToken cancellationToken = default) =>
        Inner.PurgeAsync(ids, generation, cancellationToken);

    public Task<HlcTimestamp> GetClockHighWaterAsync(CancellationToken cancellationToken = default) => Inner.GetClockHighWaterAsync(cancellationToken);
}

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

/// <summary>A local replica wired to a shared in-memory server with fault-injecting doubles.</summary>
public sealed class TestReplica
{
    public TestReplica(
        InMemorySyncServerRef server,
        string node,
        Conflicts.IConflictHandler<Note>? conflictHandler = null,
        SyncOptions<Note>? options = null,
        IPhysicalClock? physicalClock = null,
        ILocalStore<Note>? store = null,
        Func<ISyncTransport<Note>, ISyncTransport<Note>>? transport = null)
    {
        Physical = physicalClock ?? new ManualClock(1_000);
        Store = new InterceptingStore<Note>(store ?? new InMemoryLocalStore<Note>(NoteJson.Clone));
        ISyncTransport<Note> wire = new ServerRefTransport(server);
        Transport = new FaultyTransport<Note>(transport is null ? wire : transport(wire));
        Clock = new HybridLogicalClock(node, Physical);
        Engine = new SyncEngine<Note>(Store, Transport, Clock, NoteJson.Clone, conflictHandler, options);
    }

    public IPhysicalClock Physical { get; }

    public InterceptingStore<Note> Store { get; }

    public FaultyTransport<Note> Transport { get; }

    public HybridLogicalClock Clock { get; }

    public SyncEngine<Note> Engine { get; }

    public async Task<SyncRecord<Note>> RecordAsync(string id) =>
        await Engine.GetAsync(id) ?? throw new InvalidOperationException($"No record '{id}'.");
}

/// <summary>Holds a server so replicas can share it, and lets tests replace it (restore from backup).</summary>
public sealed class InMemorySyncServerRef(Server.InMemorySyncServer<Note> server)
{
    public Server.InMemorySyncServer<Note> Server { get; private set; } = server;

    /// <summary>
    /// Replaces the server with one restored from <paramref name="backup"/>, with a new epoch and a version
    /// floor above everything the replaced server issued.
    /// </summary>
    public void Restore(Server.InMemorySyncServerBackup<Note> backup, IPhysicalClock? clock = null)
    {
        var options = NoteJson.ServerOptions(clock);
        Server = new Server.InMemorySyncServer<Note>(new Server.InMemorySyncServerOptions<Note>
        {
            Cloner = options.Cloner,
            Fingerprint = options.Fingerprint,
            PhysicalClock = options.PhysicalClock,
            RestoreFrom = backup,
            VersionFloor = Server.HighestVersion,
        });
    }

    public static InMemorySyncServerRef Create(IPhysicalClock? clock = null) =>
        new(new Server.InMemorySyncServer<Note>(NoteJson.ServerOptions(clock)));

    public Note Get(string id) => Server.Snapshot().Single(n => n.Id == id);
}

/// <summary>
/// Serializes every request and response through the wire JSON encoding (source-generated metadata),
/// so the engine only ever sees what a real network peer would have sent.
/// </summary>
public sealed class JsonWireTransport<T>(ISyncTransport<T> inner, System.Text.Json.Serialization.JsonSerializerContext context) : ISyncTransport<T>
    where T : class, ISyncEntity
{
    public async Task<PullResult<T>> PullAsync(PullRequest request, CancellationToken cancellationToken = default)
    {
        var result = await inner.PullAsync(RoundTrip(request), cancellationToken);
        return RoundTrip(result);
    }

    public async Task<PushResult<T>> PushAsync(PushRequest<T> request, CancellationToken cancellationToken = default)
    {
        var result = await inner.PushAsync(RoundTrip(request), cancellationToken);
        return RoundTrip(result);
    }

    public IAsyncEnumerable<StreamEvent<T>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) =>
        inner.StreamAsync(since, cancellationToken);

    private TValue RoundTrip<TValue>(TValue value)
    {
        var typeInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)context.GetTypeInfo(typeof(TValue))!;
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        return System.Text.Json.JsonSerializer.Deserialize(bytes, typeInfo)!;
    }
}

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
