# BlazorSync

Local-first document replication for .NET and Blazor: local writes that never wait for the network,
change tracking, retry-safe push, checkpointed pull and pluggable conflict resolution.

> **Status: pre-1.0 prototype.** The protocol engine, clock and conflict handling are tested in-process.
> There is no durable store, HTTP transport, persistent server or authorization yet, so it is not ready
> for production data. See [docs/support-matrix.md](docs/support-matrix.md) and [docs/roadmap.md](docs/roadmap.md).
> Targets `net10.0`.

## What it guarantees today

Precisely, and only for the in-memory reference store and authority (details in
[docs/architecture/invariants.md](docs/architecture/invariants.md)):

- **Atomic local writes.** A write and its pending upload commit together and never wait for the network.
- **No lost local edits.** An acknowledgement, pull or conflict resolution never overwrites or marks clean
  a local edit made while the network call was in flight.
- **Retry-safe pushes.** Each write becomes an operation with a persisted id and immutable payload. If a
  response is lost, the same operation is resent and the server replays its original outcome.
- **Per-document optimistic concurrency.** The server accepts a write only if it was based on the current
  version; otherwise it returns a conflict for the client's handler.
- **Atomic, monotone pull.** A page and its checkpoint commit together; older versions never replace newer
  ones.
- **Bounded, honest runs.** Batch and retry budgets are enforced, and `SyncResult.IsComplete` says whether
  work remains.

It does **not** provide cross-document transactions, causal consistency, live notifications, offline
execution for purely server-rendered UI, or schema migration yet.

## Project layout

```
src/BlazorSync.slnx                 Solution
src/BlazorSync/                     Protocol library (engine, clock, conflicts, storage/transport contracts,
                                    in-memory reference store and authority)
src/BlazorSync.Tests/               xUnit tests: unit, regression, fault injection, seeded randomized convergence
src/BlazorSync.Demo/                Blazor WebAssembly playground simulating several devices in one tab
docs/                               Baseline review, architecture decisions, invariants, roadmap, compatibility
```

## How it works

A `SyncEngine<TDocument>` replicates one collection between a local store and a server transport.
Entities implement `ISyncEntity`:

```csharp
public interface ISyncEntity
{
    string Id { get; set; }              // stable, globally unique key (e.g. GUIDv7 assigned on create)
    HlcTimestamp UpdatedAt { get; set; } // origin timestamp: when/where the current state was authored
    bool Deleted { get; set; }           // soft-delete flag so deletions replicate
}
```

Replication metadata lives in the store's `SyncRecord<T>` envelope, not on the entity:

| Metadata | Meaning |
|---|---|
| `LocalRevision` | Incremented by every local write; decides whether an acknowledgement still applies. |
| `Pending` | The persisted operation (id, revision, base version, immutable payload) being sent. |
| `Base`, `BaseVersion` | Last confirmed server state and its server version (the concurrency token). |
| `Rejection` | Set when the server permanently rejected a revision; the record is parked until edited again. |
| Checkpoint | Opaque server-issued feed position (per store). |

### Local writes

`WriteAsync` stores a copy of the document stamped with a fresh HLC timestamp and returns a
`LocalWriteReceipt`. `DeleteAsync` stores a tombstone. Both are atomic compare-and-transform operations
on the store and never wait for replication.

### Pull

`PullAsync` asks for changes after the stored checkpoint and applies each page together with its new
checkpoint in one atomic store update. Records with unconfirmed local changes keep their local state; the
newer server state is remembered and the divergence is resolved on push.

### Push

`PushAsync` drains the pending queue in batches. For each record it first persists an operation with a
new id, then sends it with the base version it was made against. Each operation gets its own outcome:

- **Accepted**: the record adopts the server version, unless it was edited again meanwhile, in which case
  the later edit stays pending on the new base.
- **Conflict**: the configured `IConflictHandler<T>` decides.
- **Rejected**: the record is parked with `SyncRecord.Rejection` and does not block other records.
- **Retry later** or no outcome: the operation stays pending and is resent with the same id.

`SyncAsync` runs pull then push. Replication on one engine is single-flight.

### Conflict handlers

| Handler | Behaviour |
| --- | --- |
| `ClientWinsConflictHandler<T>` | **Default.** Local change is re-pushed over the concurrent server change (the remote edit is lost). |
| `ServerWinsConflictHandler<T>` | Server state wins; the conflicting local change is discarded. |
| `LastWriteWinsConflictHandler<T>` | The later *authoring* timestamp wins, independent of upload order. Depends on roughly synchronized clocks. |
| `DelegateConflictHandler<T>` | Wraps a function for custom merges. |

A handler receives copies of `RealMaster` (server current), `AssumedMaster` (the base of the local edit)
and `Fork` (latest local state) and returns `AcceptMaster()`, `KeepFork()` or `Resolve(merged)`. Handlers
must be deterministic and side-effect free; they never run for a replayed outcome.

## Getting started

```csharp
using BlazorSync;
using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Server;
using BlazorSync.Storage;

// 1. A clock with a stable, persisted, per-replica node id ([A-Za-z0-9._~-], up to 64 chars).
var clock = new HybridLogicalClock(node: "device-a");

// 2. Local store + a transport to the server (in-memory reference implementations).
var store = new InMemoryLocalStore<Note>();
var server = new InMemorySyncServer<Note>();
var transport = new InProcessTransport<Note>(server);

// 3. The engine.
var engine = new SyncEngine<Note>(store, transport, clock,
    conflictHandler: new LastWriteWinsConflictHandler<Note>());

// 4. Local-first writes (committed locally, queued for upload).
LocalWriteReceipt receipt = await engine.WriteAsync(new Note { Title = "Hello", Body = "world" });

// 5. Sync when connectivity allows, and check whether everything was done.
SyncResult result = await engine.SyncAsync();
if (!result.IsComplete) { /* work remains: deferred, rejected or over budget */ }

// 6. Read what the app sees.
IReadOnlyList<Note> notes = await engine.QueryAsync();
```

```csharp
public sealed class Note : ISyncEntity
{
    public string Id { get; set; } = Guid.CreateVersion7().ToString();
    public HlcTimestamp UpdatedAt { get; set; }
    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
```

## Trimming and AOT (Blazor WebAssembly)

The engine, in-memory store and in-memory server default to reflection-based `System.Text.Json` for
cloning (and, on the server, for operation fingerprints). That default is **not** trim/AOT-safe. For
published WebAssembly builds supply explicit delegates, as the demo does:

```csharp
var options = new SyncOptions<DemoNote> { Cloner = doc => doc.Clone() };
var engine = new SyncEngine<DemoNote>(store, transport, clock, options: options);

var server = new InMemorySyncServer<DemoNote>(new InMemorySyncServerOptions<DemoNote>
{
    Cloner = n => n.Clone(),
    Fingerprint = DemoJsonContext.Fingerprint, // source-generated JSON
});
```

## Hybrid Logical Clock

`HlcTimestamp(WallTime, Counter, Node)` is validated on construction (wall time ≤ 15 digits, counter ≤
999,999, ASCII node alphabet), so numeric order and the ordinal order of `Encode()` always agree.
`HybridLogicalClock` is thread-safe and strictly monotonic; counter overflow carries into wall time.
The engine seeds its clock from the store's high-water mark before its first write, so timestamps are not
reused after a restart. The server validates that timestamps are not too far in the future and never
re-stamps them.

## The demo

`src/BlazorSync.Demo` is a Blazor WebAssembly playground that simulates several devices in one browser
tab, each with its own engine and clock, talking to one in-process server. Nothing is persisted.

- **Playground** (`/playground`): create, edit and delete notes per device; toggle devices offline.
- **Conflict Lab** (`/conflicts`): force concurrent edits and compare conflict strategies.
- **Clock Explorer** (`/clock`): visualize HLC timestamp generation.

```bash
dotnet run --project src/BlazorSync.Demo
```

## Building and testing

```bash
dotnet build src/BlazorSync.slnx -c Release
dotnet test src/BlazorSync.slnx -c Release
dotnet publish src/BlazorSync.Demo -c Release                              # optional
dotnet publish src/BlazorSync.Demo -c Release -p:RunAOTCompilation=true    # needs the wasm-tools workload
```

Test display names carry invariant (`I04`) and catalogue (`T11`) ids, for example:

```bash
dotnet test src/BlazorSync.slnx --filter "DisplayName~I04"
```

## Documentation

- [Baseline review](docs/review/baseline.md): reproduced defects and what changed.
- [Architecture decisions and invariants](docs/architecture/README.md).
- [Compatibility policy and migration notes](docs/compatibility.md).
- [Support matrix](docs/support-matrix.md) and [roadmap](docs/roadmap.md).

## License

MIT. See [LICENSE](LICENSE).
