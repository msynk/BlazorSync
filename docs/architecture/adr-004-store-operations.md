# ADR-004: Durable store operations and ownership

- **Status:** Accepted for the contract; provider details pending Phases 3 and 5 (2026-09-27)
- **Invariants:** I01, I02, I03, I15

## Context

`ILocalStore` exposed `GetAsync` + `UpsertAsync` + `SetCheckpointAsync`. The engine composed read/modify/
write sequences across awaits, so any concurrent local edit could be overwritten (S02, S03), and a pull
page and its checkpoint could be persisted separately.

## Alternatives

1. **Generic transaction object** (`BeginTransaction`, reads and writes, `Commit`). Rejected: cannot be
   held open across .NET/JS awaits in IndexedDB, and invites long transactions spanning network I/O.
2. **Many coarse commands** (`AcknowledgeAsync`, `ApplyPageAsync`, …). Clear but pushes protocol logic
   into every provider and multiplies conformance surface.
3. **One atomic multi-record compare-and-transform plus optional checkpoint.** Chosen.

## Decision

- `UpdateAsync(IReadOnlyList<RecordUpdate>, Checkpoint?)` applies pure transforms to the committed state
  of one or more records and optionally stores the checkpoint, atomically. Transforms receive copies,
  return a new record or `null` (no change), must not call the store, and may run more than once, which
  allows optimistic implementations (read, compute, conditional write, retry) for IndexedDB.
- All protocol state changes are expressed as transforms conditioned on local revision or pending
  operation id: local write, delete, operation preparation, acknowledgement, rejection, conflict
  resolution, pull page.
- Reads: `GetAsync`, `GetPendingAsync(limit, exclude)` (bounded, ordered by origin time then id),
  `CountDirtyAsync`, `QueryAsync`, `GetCheckpointAsync`, `GetClockHighWaterAsync`.
- The store maintains a clock high-water mark on commit (I12).
- Stores never alias application objects: values are copied on the way in and out.
- **Ownership:** one engine replicates a store at a time. Within one engine, replication is single-flight;
  local writes do not take the replication gate and never wait for network I/O. Multi-tab/multi-process
  ownership (leases, fencing) is a provider concern designed in Phase 5.
- **Queue coalescing:** a record has at most one pending operation. Unsent edits coalesce into the
  current state; once an operation is persisted it is immutable and resent unchanged until final. A later
  edit is sent as the next operation after the first is acknowledged.

## Consequences

- Durable providers must implement `UpdateAsync` as one database transaction (SQLite) or one readwrite
  IndexedDB transaction with conditional writes.
- `QueryAsync` is unbounded today; a bounded query subset is Phase 3/6 work.

## Migration impact

Breaking for custom `ILocalStore` implementations: `UpsertAsync`, `SetCheckpointAsync` and
`GetDirtyAsync` were replaced by `UpdateAsync`, `GetPendingAsync`, `CountDirtyAsync` and
`GetClockHighWaterAsync`. See `docs/compatibility.md`.

## Tests

`ThrowingTransformIsAtomic`, `CrashDuringPageApplyIsAtomic`, `CheckpointCommittedWithPage`,
`StoreSnapshotsDoNotAlias`, `S02`, `S03`. A shared provider conformance suite is Phase 2/3 work.
