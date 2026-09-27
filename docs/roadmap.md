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
| 2: Protocol v1 and durable-state contracts | **In progress** | Done: metadata separation, operation ids with persisted immutable payloads, reference authority receipts with fingerprints, per-operation outcomes, outcome correlation and validation, epochs, fault injection at every commit boundary, seeded randomized convergence. Remaining below. |
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

1. **Phase 2 — wire specification and fixtures.** Write `docs/protocol/v1.md` (normative, MUST/SHOULD)
   covering the messages in `src/BlazorSync/Protocol`, protocol/schema version negotiation, error
   taxonomy (`PushErrorCodes` + transport-level errors: upgrade required, reset required, unauthorized,
   payload too large, rate limited with `Retry-After`), and JSON encoding rules from ADR-011 (64-bit
   integers as strings, canonical HLC encoding, null vs absent, unknown fields). Add
   `docs/protocol/fixtures/*.json` and a test that round-trips each fixture through a source-generated
   `JsonSerializerContext`, including non-ASCII ids and `9007199254740993`-sized versions.
2. **Phase 2 — provider conformance suite.** Extract the store behaviours tested today into an abstract
   `LocalStoreConformanceTests<TStore>` (atomic multi-record update, checkpoint atomicity, copy isolation,
   pending ordering and exclusion, high-water mark, transform re-invocation safety) and an
   `AuthorityConformanceTests` for push/pull semantics. Run both against the in-memory implementations.
   These become the gate for SQLite, IndexedDB and PostgreSQL.
3. **Phase 2 — AOT/trimming honesty.** Replace the `#pragma` suppressions around reflection defaults with
   `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` on reflection-default constructors, and provide
   overloads that take required cloner/fingerprint delegates or a `JsonTypeInfo<T>`. Verify with
   `dotnet publish src/BlazorSync.Demo -c Release -p:RunAOTCompilation=true` (currently warning-free) and a
   trimmed console app with `IsAotCompatible`.
4. **Phase 2 — stateful model test.** Extend `RandomizedConvergenceTests` with a reference model of
   expected per-document outcomes (not just convergence), random batch sizes, concurrent local writes during
   flights, and a failing-seed printer.
5. **Phase 3 — SQLite store.** New project `BlazorSync.Storage.Sqlite` (Microsoft.Data.Sqlite): tables
   for records, pending operation payloads, checkpoint, metadata (schema version, replica id, incarnation,
   clock high-water); `UpdateAsync` as one `BEGIN IMMEDIATE` transaction; indexes for pushable ordering;
   process-kill tests (child process killed between commits, then reopen).
6. **Phase 4** requires a PostgreSQL instance (local, container or CI service) before any concurrency or
   ordering claim; see ADR-005 and ADR-009.

## Known limitations today

- Everything is in memory; nothing survives a process restart.
- No authentication, authorization or scopes (I07, I18 open).
- No reset/resnapshot flow: an epoch change surfaces `SyncResetRequiredException` (I14 open).
- Full-document replacement drops fields unknown to an older writer (I17 open).
- `ClientWins` remains the default conflict policy and discards the concurrent remote edit.
- Unbounded operation receipt retention in the reference authority.
- The demo is a single-tab simulation and has not been run in a browser in this change.
