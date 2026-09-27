# Architecture

- [Invariants I01–I20 and their current status](invariants.md)

## Decision records

| ADR | Topic | Status |
|---|---|---|
| [001](adr-001-build-versus-adopt.md) | Build a native protocol vs adopt Datasync, Dotmim.Sync, PowerSync, RxDB, Replicache/Zero, Fusion, Electric | Accepted |
| [002](adr-002-consistency-model.md) | Consistency model and scope | Accepted |
| [003](adr-003-metadata-separation.md) | Local revision, operation id, server version, checkpoint, origin HLC | Accepted |
| [004](adr-004-store-operations.md) | Atomic store operations, ownership and queue coalescing | Accepted (contract) |
| [005](adr-005-feed-ordering.md) | Committed-prefix feed ordering and epochs | Accepted (reference); database algorithm proposed |
| [006](adr-006-conflict-model.md) | Conflict model, policies and defaults | Accepted (Phase 1); default change proposed |
| [007](adr-007-hosting-and-lifecycle.md) | Hosting profiles, application API, DI and lifecycle | Proposed |
| [008](adr-008-browser-storage.md) | IndexedDB baseline, OPFS/SQLite only on evidence | Proposed |
| [009](adr-009-server-integration.md) | Controlled write service, PostgreSQL first, capture coverage | Proposed |
| [010](adr-010-auth-and-scope.md) | Authentication, scope identity, revocation and account switching | Proposed |
| [011](adr-011-compatibility.md) | Wire, store and domain schema compatibility | Accepted in principle |
| [012](adr-012-packaging-and-support.md) | Packaging, support tiers, target framework, workloads | Accepted |

## Current shape (after Phase 1)

```
            local writes (atomic, never wait for network)
 app ──► SyncEngine ──────────────────────────────────► ILocalStore.UpdateAsync (compare-and-transform)
            │  single-flight replication                   records: Current, Base, BaseVersion,
            │                                              LocalRevision, Pending, Rejection, Observed
            ▼                                              + checkpoint + clock high-water mark
      ISyncTransport ──► authority (InMemorySyncServer today)
         pull: opaque checkpoint → page of (document, version)
         push: operations (id, base version, payload) → one outcome per id
               accepted | conflict | rejected | retry-later, duplicates replayed from receipts
```
