# BlazorSync

Platform-independent, local-first sync protocol with change tracking and conflict resolution for Blazor (WebAssembly, Hybrid, and native .NET).

BlazorSync implements an RxDB-style replication protocol in C#. All of the protocol logic — checkpoint iteration, change tracking, and conflict resolution — lives on the client, so storage backends and the server stay deliberately simple ("complexity in the client, dumb backend"). Writes are causally ordered with a Hybrid Logical Clock, deletions are soft so they replicate, and the server is always authoritative over write timestamps.

> Targets `net10.0`.

## Why local-first

- **Offline by default.** All reads and writes hit a local store. Sync happens opportunistically in the background.
- **Causal ordering without trusted clocks.** A Hybrid Logical Clock (HLC) produces strictly monotonic, totally-ordered timestamps that stay meaningful even when device clocks drift, giving deterministic conflict resolution across devices.
- **Pluggable everything.** The engine depends only on three abstractions — a local store, a transport, and a conflict handler — so you can swap SQLite, IndexedDB/OPFS, HTTP, or an in-process server without touching protocol code.

## Project layout

```
BlazorSync.slnx
├── src/BlazorSync            The protocol library (engine, clock, conflicts, storage/transport contracts)
├── samples/BlazorSync.Demo        Blazor WebAssembly multi-device playground
└── tests/BlazorSync.Tests    xUnit tests for the clock, conflicts, and sync engine
```

## How it works

A `SyncEngine<TDocument>` orchestrates replication of a single collection between a local store and a server transport. Every synchronized type implements `ISyncEntity`, which carries the minimal metadata the protocol needs:

```csharp
public interface ISyncEntity
{
    string Id { get; set; }            // stable, globally unique key (e.g. a GUIDv7 assigned on create)
    HlcTimestamp UpdatedAt { get; set; } // HLC timestamp of the last write; also the sync cursor
    bool Deleted { get; set; }          // soft-delete flag so deletions replicate
}
```

### Local writes

`WriteAsync` and `DeleteAsync` stamp the document with a fresh HLC timestamp, mark it dirty (queued for push), and preserve the last-known server baseline so a future conflict can be detected against it. Deletions are soft: the record is retained with `Deleted = true`.

### Pull (catch-up)

`PullAsync` repeatedly fetches batches strictly after the stored `Checkpoint` and applies them until the server reports no more changes. Records are ordered deterministically by `(UpdatedAt, Id)`, so a peer resumes from exactly where it left off with no gaps or duplicates. Dirty local records are left untouched — their divergence surfaces during the next push.

### Push (send and resolve)

`PushAsync` sends dirty records, each carrying the `AssumedMaster` (the server state the client believed was current) alongside the new state. The server accepts the write only if the assumed master still matches its current version; otherwise it returns the real master as a conflict. Accepted writes adopt the server-stamped timestamp; conflicts are resolved by the configured `IConflictHandler<TDocument>`. Because a resolution can produce a merged document that must itself be pushed, push runs multiple passes until the queue drains (bounded by `SyncOptions.MaxPushPasses`).

`SyncAsync` runs a full cycle: pull, then push, returning an aggregate `SyncResult` (pulled / pushed / conflicts).

### Conflict handlers

The engine ships with several built-in strategies (all in `BlazorSync.Conflicts`):

| Handler | Behavior |
| --- | --- |
| `ClientWinsConflictHandler<T>` | **Default.** Local change wins and is re-pushed over the concurrent server change. |
| `ServerWinsConflictHandler<T>` | Server's current state wins; conflicting local changes are discarded. |
| `LastWriteWinsConflictHandler<T>` | Greater `UpdatedAt` wins (deterministic via HLC total order); ties fall back to the server. |
| `DelegateConflictHandler<T>` | Wraps a `Func<ConflictContext<T>, ConflictResolution<T>>` for custom/field-level merges. |

A handler receives the `RealMaster` (server state), the `AssumedMaster` (what the client thought was current), and the `Fork` (local state), and returns either `AcceptMaster()` or `Resolve(merged)`.

## Getting started

Reference the core project and wire up an engine. The example below uses the included in-memory store and in-process server, which is also how the test suite runs.

```csharp
using BlazorSync;
using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Server;
using BlazorSync.Storage;

// 1. A node-unique clock (use a stable, persisted id per device/installation).
var clock = new HybridLogicalClock(node: "device-a");

// 2. Local store + a transport to the server.
var store  = new InMemoryLocalStore<Note>();
var server = new InMemorySyncServer<Note>();
var transport = new InProcessTransport<Note>(server);

// 3. The engine.
var engine = new SyncEngine<Note>(
    store,
    transport,
    clock,
    conflictHandler: new LastWriteWinsConflictHandler<Note>());

// 4. Local-first writes.
await engine.WriteAsync(new Note { Title = "Hello", Body = "world" });

// 5. Sync (pull + push) when connectivity allows.
SyncResult result = await engine.SyncAsync();
// result.Pulled / result.Pushed / result.Conflicts

// 6. Read what the app sees.
IReadOnlyList<Note> notes = await engine.QueryAsync();
```

### Building your own entity

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

The engine, in-memory store, and in-memory server default to a reflection-based `System.Text.Json` deep clone (`DocumentCloner.JsonClone`) to keep the "current" and "base" states isolated. That default is **not** trim/AOT-safe. For published WebAssembly builds, supply a hand-written `Cloner` via `SyncOptions<T>` (and to the store/server constructors). The demo's `DemoNote.Clone()` shows the recommended pattern:

```csharp
var options = new SyncOptions<DemoNote> { Cloner = doc => doc.Clone() };
var engine = new SyncEngine<DemoNote>(store, transport, clock, options: options);
```

## Hybrid Logical Clock

`HlcTimestamp(WallTime, Counter, Node)` combines a physical wall-clock component (Unix ms) with a monotonic logical counter and a node id. Timestamps are totally ordered (wall time, then counter, then node) and `Encode()`/`Parse()` round-trip to a lexicographically sortable string — usable directly as a sortable DB column and as a checkpoint cursor. `HybridLogicalClock` is thread-safe: call `Now()` for local events and `Update(remote)` when receiving a peer timestamp.

## The demo

`samples/BlazorSync.Demo` is a Blazor WebAssembly playground that runs multiple virtual "devices" in the browser, each with its own engine and clock, all talking to a shared in-process server. It includes:

- **Playground** (`/playground`) — create/edit/delete notes per device and watch them sync.
- **Conflict Lab** (`/conflicts`) — force concurrent edits and switch conflict strategies to see resolution in action.
- **Clock Explorer** (`/clock`) — visualize HLC timestamp generation.

Run it:

```bash
dotnet run --project samples/BlazorSync.Demo
```

## Building and testing

```bash
dotnet build
dotnet test
```

The test suite (`tests/BlazorSync.Tests`) covers the Hybrid Logical Clock, the built-in conflict handlers, and end-to-end sync engine scenarios using a `ManualClock` for deterministic timestamps.

## Status

The in-memory server documents the contract a real backend must honor; an EF Core–backed server over an arbitrary database is planned. Live change streaming (`ISyncTransport.StreamAsync` / `StreamEvent`) is defined but not yet implemented for the in-process transport — until then, clients use checkpoint-iteration pull.
