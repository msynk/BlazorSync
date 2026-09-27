# ADR-008: Browser storage

- **Status:** Proposed; implementation in Phase 5 (2026-09-27)
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
- Multi-tab: leadership via Web Locks where available, plus store-level fencing (a lease generation
  checked in every write transaction) so a stale leader cannot acknowledge.
- Quota, eviction and private-mode failures are reported as errors, never as durable success. Request
  `navigator.storage.persist()` where appropriate and report the result.
- OPFS/SQLite only after a benchmark shows a workload that needs it (Phase 5 task 8 / Phase 11).
- Native SQLite (Phase 3) is a separate provider and is not a simulation of browser or server databases.

## Tests (to be written)

Shared store conformance in real Chromium, Firefox and WebKit (T41–T45). Native Safari/device coverage is
reported separately from WebKit automation.
