using Bsync.Clocks;
using Bsync.Storage;
using Bsync.Transport;

namespace Bsync.Tests.TestSupport;

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
