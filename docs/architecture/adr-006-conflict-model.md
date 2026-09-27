# ADR-006: Conflict model and defaults

- **Status:** Accepted for Phase 1; default change proposed for Phase 8 (2026-09-27)
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
- **Built-in policies:** `ClientWins` (default, lossy for the remote edit, upload-order dependent),
  `ServerWins` (lossy for the local edit), `LastWriteWins` (lossy, upload-order independent, depends on
  roughly synchronized clocks), `Delegate` (custom).
- **Server rejections** (validation, authorization, clock skew) are not conflicts. The record is parked
  with `SyncRecord.Rejection` until a new local write replaces it.

## Proposed for Phase 8

- Change the default to a conservative policy that preserves both sides: keep the server state visible
  and retain the local fork as a durable **unresolved conflict** for the application or user to resolve.
  This is a behaviour change and will ship with migration notes and an opt-in period.
- Field-level three-way merge helpers with defined semantics for null vs absent, arrays, sets and
  counters.
- An explicit restore operation for deleted documents.

## Alternatives

CRDTs for business documents (rejected for v1; metadata growth and schema interplay, see Phase 11);
server-side resolution (possible later for command-based writes).

## Migration impact

`LastWriteWinsConflictHandler` now returns `KeepFork` instead of `Resolve(fork)`. Custom handlers are
unaffected unless they relied on receiving the fork state of the operation rather than the latest local
state.

## Tests

`S04`, `S10`, `ConflictBudget`, `EditDuringConflictResolution`, `MergeResolutionIsPushed`,
`DeleteVersusUpdate`, `ConflictHandlerTests`.
