# ADR-006: Conflict model and defaults

- **Status:** Accepted (Phase 1); Phase 8 decisions accepted 2026-09-28
- **Invariants:** I02, I10, I11, I19

## Context

Conflicts are detected by the server (base version ≠ current) and resolved on the client by an
`IConflictHandler`. The prototype re-ran the handler for a replayed write (S04), applied a resolution
computed from an older local state to a newer one, and made LWW depend on upload order (S10).

## Decision (Phase 1)

- The handler receives copies of **RealMaster** (server current), **AssumedMaster** (the base the local
  edit was made against) and **Fork** (latest local state, including edits made after the operation was
  sent). It returns one of:
  - `AcceptMaster()`: discard local change (lossy).
  - `KeepFork()`: re-push the local state unchanged, keeping its origin timestamp.
  - `Resolve(doc)`: a new local edit, re-stamped with a fresh HLC after the master's timestamp.
- The handler must be deterministic and free of external side effects. It never runs for a replayed
  (duplicate) outcome of an operation that was already decided.
- A resolution is committed only if the local revision is unchanged since the handler read it; otherwise
  the stale operation is dropped, the base is kept, and the next push re-runs the policy on the newer
  state.
- Each document may conflict at most `MaxConflictRetries` times per push; then it is deferred so
  unrelated documents progress (I19).
- **Delete vs update** is a normal conflict: tombstones are documents with `Deleted = true`. LWW compares
  timestamps regardless of deletion; server-wins and client-wins behave as named.
- **Built-in policies:** `ClientWins` (the Phase 1 default, lossy for the remote edit, upload-order dependent),
  `ServerWins` (lossy for the local edit), `LastWriteWins` (lossy, upload-order independent, depends on
  roughly synchronized clocks), `Delegate` (custom).
- **Server rejections** (validation, authorization, clock skew) are not conflicts. The record is parked
  with `SyncRecord.Rejection` until a new local write replaces it.

## Decision (Phase 8)

- **Conservative default.** The default handler is `DeferConflictHandler`: the handler outcome `Defer` makes
  the replica adopt the server state (clean, so nothing is pushed) and keep the local change durably in
  `SyncRecord.Conflict` (`SyncConflict<T>`: server state and version at detection, the local change, and the
  common ancestor when known). Nothing is lost and nothing is overwritten until the application or user
  decides. Unrelated documents keep syncing (I19).
- **Deciding.** `SyncEngine.GetConflictsAsync`, `ResolveConflictAsync(id, resolved)` (a new local edit based on
  the newest server state the replica knows, pushed like any write) and `DiscardConflictAsync(id)` (keep the
  server state). The Blazor facade exposes the same three operations on `ISyncCollection<T>` and reports
  `SyncItemState.Conflicted`; server-connected collections never keep conflicts, because their writes are
  answered with `SyncConfirmation.Conflict` at once. A later ordinary write to the document does not clear a
  kept conflict: the decision stays pending until it is made explicitly.
- **Persistence.** Stores persist the conflict with the record and list conflicts by id (SQLite schema 2,
  IndexedDB schema 2; both migrations are additive and tested with pending work). A kept conflict counts as
  local data: a purge after a scope change or retention reset never removes such a record; it is hidden like
  a missing record and stays listed (I01, I10).
- **Field-level three-way merge.** `ThreeWayMerge.Merge(base, local, server, typeInfo)` merges through the
  JSON form with source-generated metadata (AOT-safe). Rules: a member changed on one side takes that side;
  equal changes agree; different changes of one member are a conflict reported as a JSON Pointer, with the
  server value kept. Objects merge per member; an absent member differs from `null`; arrays, strings and
  numbers are atomic, so sets and counters are **not** merged semantically (use a custom handler);
  `[JsonExtensionData]` members merge like known ones; the timestamp is not merged; delete versus any content
  change is a conflict (`(deleted)`). `ThreeWayMergeConflictHandler` pushes clean merges and hands everything
  else (field conflicts, unknown ancestor) to a fallback, `Defer` by default.
- **Restoring deleted documents.** A tombstone is an ordinary document: writing the id again restores it.
  After the authority purged the tombstone, an edit based on the purged version is rejected with
  `base-expired` (the replica clears its base), and writing the document again recreates it explicitly
  (protocol §4).

## Not decided yet

- Dependency groups (several documents that must be applied together) and fairness between them (I19).
- Semantic merges for counters and sets.

## Alternatives

CRDTs for business documents (rejected for v1; metadata growth and schema interplay, see Phase 11);
server-side resolution (possible later for command-based writes).

## Migration impact

Phase 8: the default policy changed from `ClientWins` to `Defer`. Applications that relied on local edits
overwriting concurrent server edits pass `new ClientWinsConflictHandler<T>()` explicitly; all others should
show conflicts to the user (`GetConflictsAsync`) or install `ThreeWayMergeConflictHandler`.

Phase 1: `LastWriteWinsConflictHandler` now returns `KeepFork` instead of `Resolve(fork)`. Custom handlers are
unaffected unless they relied on receiving the fork state of the operation rather than the latest local
state.

## Tests

`S04`, `S10`, `ConflictBudget`, `EditDuringConflictResolution`, `MergeResolutionIsPushed`,
`DeleteVersusUpdate`, `ConflictHandlerTests`; Phase 8: `ConflictResolutionTests` (default keeps, resolve,
discard, restart on SQLite, SQLite migration), `ThreeWayMergeTests`, `SelectiveSyncTests.RevokedConflictIsKept`,
`BlazorIntegrationTests.ConflictsThroughCollection`, `NotesSampleTests.ConflictIsShownAndResolved` (two browser devices, Chromium/Firefox/WebKit), store conformance cases for conflicts and purge, and the
IndexedDB schema migration browser test.
