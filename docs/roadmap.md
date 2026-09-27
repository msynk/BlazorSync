# Roadmap

Phases follow the implementation plan. Each lists its status, the invariants it owns and the next
concrete work item. Test ids (T01–T60) refer to the adversarial catalogue; invariants (I01–I20) to
[`architecture/invariants.md`](architecture/invariants.md).

## v1 product focus

Authorized application documents replicated between durable client replicas (IndexedDB in browsers,
SQLite natively) and one ASP.NET Core authority backed by PostgreSQL, with offline local writes where code
runs on the device and equivalent contracts for server-connected and request-rendered hosts. Initial
workloads and targets: ADR-012. Explicit non-goals: see the README.

## Status

| Phase | Status | Summary |
|---|---|---|
| 0: Evidence and decisions | **Done** | `docs/review/baseline.md`, ADR-001–012, support matrix, compatibility policy, this roadmap. |
| 1: Immediate core safety | **Done** | All ten baseline scenarios fixed with regression tests; atomic store transforms; single-flight replication; validated options, ids and HLC; truthful `SyncResult`. |
| 2: Protocol v1 and durable-state contracts | **Nearly done** | Done: metadata separation, persisted immutable operations, receipts with fingerprints, per-operation outcomes, outcome/page validation, epochs, normative spec `docs/protocol/v1.md` with valid/invalid fixtures, strict source-generated JSON encoding, store and authority conformance suites, AOT-honest constructors, fault injection at commit boundaries, seeded randomized tests with a reference model. Remaining: reset/resnapshot specification and engine flow (item 1 below).
| 3: Durable local storage (SQLite) | Not started | |
| 4: Authority, PostgreSQL, HTTP | Not started | Needs PostgreSQL in CI or locally. |
| 5: IndexedDB, WASM/PWA slice | Not started | Needs Playwright browsers. |
| 6: Blazor host integration | Not started | |
| 7: Session lifecycle and notifications | Not started | |
| 8: Conflicts, selective sync, retention | Not started | Includes the proposed conservative default conflict policy (ADR-006). |
| 9: Migrations and disaster recovery | Not started | |
| 10: Operational quality and release | Not started | |
| 11: Evidence-selected extensions | Not started | |

## Handoff: next work items, in order

1. **Phase 2 — reset/resnapshot (I14).** Specify in `docs/protocol/v1.md` §6 and implement:
   - Store the epoch alongside `BaseVersion`/`ObservedVersion` (or clear version guards on reset), because
     versions from a new epoch may be lower than the old ones and the monotone guard would ignore them.
   - On `SyncResetRequiredException`: keep dirty records and their pending operations untouched; restart
     the pull from `Checkpoint.Start` in a *resnapshot* mode that records which clean records were seen.
   - When the snapshot completes, clean records that were not seen become "missing after reset": remove
     them from the local projection (not a global delete, I10) and report them.
   - Pending operations are resent unchanged; the new authority either accepts them, conflicts, or replays
     receipts it still has. Add tests T35 and a crash-mid-resnapshot test.
2. **Phase 3 — SQLite store.** New project `BlazorSync.Storage.Sqlite` (Microsoft.Data.Sqlite): tables for
   records, pending payloads, checkpoint and metadata (schema version, replica id, incarnation, clock
   high-water); `UpdateAsync` as one `BEGIN IMMEDIATE` transaction with optimistic re-read; indexes for
   pushable ordering. Subclass `LocalStoreConformanceTests`. Add child-process kill/reopen tests (T16–T20,
   T51) and multiple-connection concurrency tests.
3. **Phase 3 — replica identity.** Replica id + incarnation in store metadata; a restored/cloned store gets
   a new incarnation and a new clock node (S08 variant for clones).
4. **Phase 4** requires a PostgreSQL instance (local, container or CI service) before any concurrency or
   ordering claim; see ADR-005, ADR-009 and `docs/protocol/v1.md` §8. Its authority must pass
   `AuthorityConformanceTests` plus multi-instance and commit-order tests.
5. **Housekeeping.** Move the conformance suites and fault doubles into a `BlazorSync.Testing` package when
   the second provider exists; add CI (build, test, AOT publish).

## Known limitations today

- Everything is in memory; nothing survives a process restart.
- No authentication, authorization or scopes (I07, I18 open).
- No reset/resnapshot flow: an epoch change surfaces `SyncResetRequiredException` (I14 open).
- Full-document replacement drops fields unknown to an older writer (I17 open).
- `ClientWins` remains the default conflict policy and discards the concurrent remote edit.
- Unbounded operation receipt retention in the reference authority.
- The reference authority accepts a non-null base version for a document it does not have (under review,
  see `docs/protocol/v1.md` §4).
- The demo is a single-tab simulation and has not been run in a browser in this change.
