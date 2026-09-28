# ADR-008: Browser storage

- **Status:** Accepted and implemented (`BlazorSync.Storage.IndexedDb`, 2026-09-27)
- **Invariants:** I01, I02, I03, I14

## Context

Browser replicas need transactional persistence reachable from .NET WebAssembly through JS interop,
across Chromium, Firefox and WebKit, with multiple tabs.

## Alternatives

1. **IndexedDB through a narrow JS bridge.** Universally available, transactional per object-store set,
   auto-commits when the event loop has no pending requests (so a transaction cannot span arbitrary
   .NET awaits).
2. **SQLite compiled to WASM with OPFS persistence.** Relational queries and larger datasets; requires a
   worker, specific VFS choices and, for some VFSs, cross-origin isolation headers. Portability must be
   measured.
3. **localStorage.** Synchronous, tiny, no transactions. Rejected.

## Decision

- Baseline browser provider: IndexedDB. `ILocalStore.UpdateAsync` maps to: one readwrite transaction to
  read the addressed records, compute transforms in .NET, then a second readwrite transaction that writes
  only if each record's local revision/pending id is unchanged (optimistic retry). Records, checkpoint and
  high-water mark live in object stores written in that same transaction.
- One database per account and scope; versioned schema with `onupgradeneeded` migrations and
  `onblocked` handling surfaced to the app.
- Multi-tab: leadership via Web Locks where available, plus store-level optimistic concurrency (a write
  stamp per record checked in every write transaction) so no tab, stale leader or not, can commit over a
  newer state.
- Quota, eviction and private-mode failures are reported as errors, never as durable success. Request
  `navigator.storage.persist()` where appropriate and report the result.
- OPFS/SQLite only after a benchmark shows a workload that needs it (Phase 5 task 8 / Phase 11).
- Native SQLite (Phase 3) is a separate provider and is not a simulation of browser or server databases.

## Implementation notes

- One database per account namespace (`IndexedDbStoreOptions.DatabaseName`), all collections in one
  `records` store keyed by `[collection, id]` plus a `meta` store; sparse indexes for pending, dirty, stale,
  visible and live records. IndexedDB compares strings by UTF-16 code unit, so id order equals .NET ordinal.
- `UpdateAsync`: read with per-record write stamps, run transforms in .NET, commit in one readwrite
  transaction (`durability: "strict"`) that aborts if any stamp changed; retry with jittered backoff. Writers
  in one tab are serialized by a gate so only other tabs cause retries. The stored generation cannot
  decrease (a stale second replication session fails with `stale-generation`).
- 64-bit numbers cross the JS boundary as decimal strings; documents are JSON produced by a source-generated
  `JsonTypeInfo<T>`; no reflection-based serialization.
- A newer schema opened by another tab closes this tab's connection (`onversionchange`); later calls fail
  with `outdated` rather than blocking the upgrade. Missing IndexedDB, quota and blocked upgrades surface as
  `LocalStoreUnavailableException`.
- Tab ownership: `IndexedDbReplicaLease` (Web Locks, released automatically when the tab closes or
  crashes). Correctness does not depend on it; it avoids duplicate replication work.
- `RequestPersistenceAsync` must not be awaited on startup: Firefox answers with a permission prompt.

## Tests

`BlazorSync.Tests.Browser` (Playwright) runs, in Chromium, Firefox and WebKit builds: the shared store
conformance cases; offline edits across a tab reload and convergence with a second browser profile; an
acknowledgement in one tab never cleaning another tab's newer edit; 40 concurrent writes from two tabs
yielding exactly revisions 1..40 (a mutation removing the stamp check fails it); lease exclusivity and
release on tab close; unavailable IndexedDB; an upgrade by another tab; a deleted database detected by a new
replica id and repopulated; reset after a server restore. The same suite also passed against a WebAssembly
AOT build of the harness. Native Safari, iOS/Android browsers and real quota pressure are not covered.

## OPFS/SQLite (task 5.8)

Not implemented. No workload so far needs relational queries or datasets beyond what IndexedDB handles in
the tests. Revisit with a benchmark (dataset from ADR-012) that shows IndexedDB missing a latency or size
target; record the VFS, worker and cross-origin-isolation requirements then.
