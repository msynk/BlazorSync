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
| 2: Protocol v1 and durable-state contracts | **Done** | Metadata separation, immutable operations, receipts, per-operation outcomes, normative spec and fixtures, strict JSON, conformance suites, AOT-honest API, reset/resnapshot with generations and authority restore rules (`docs/protocol/v1.md` §6.1). |
| 3: Durable local storage (SQLite) | **Done for Windows** | `BlazorSync.Storage.Sqlite`: transactional store, schema versioning, replica identity/incarnation, conformance, process-kill tests, post-commit observation (`SyncEngine.Observe`). Schema 2 with an in-place, pending-work-preserving migration (Phase 8). Remaining: runs on Linux/macOS/mobile. |
| 4: Authority, PostgreSQL, HTTP | **HTTP half done** | `ISyncAuthority` shared by HTTP and in-process callers, scopes and read/write authorization hooks, `BlazorSync.Server.AspNetCore` endpoints, `BlazorSync.Transport.Http` client; conformance over HTTP. Remaining: the PostgreSQL authority (needs a PostgreSQL instance), token refresh and cookie/CORS guidance with a real host, multi-instance tests. |
| 5: IndexedDB, WASM/PWA slice | **Done (with stated gaps)** | `BlazorSync.Storage.IndexedDb` (optimistic cross-tab commits, Web Locks lease, upgrade/unavailable handling), Playwright suite in Chromium/Firefox/WebKit (also against a WebAssembly AOT build), notes PWA sample (server + offline client) with offline reload, service-worker update and server-restart tests. Gaps: offline app-shell reload unverified in WebKit (Playwright limitation), no native Safari/mobile runs, no real quota-pressure test, OPFS not pursued (ADR-008). |
| 6: Blazor host integration | **Done for web hosts** | `BlazorSync.Blazor` (collection API, local session, server-connected collection, explicit recipes incl. `AddBrowserSyncCollection`); Blazor Web App sample (static SSR, Interactive Server, WebAssembly, Auto) with Playwright tests; notes PWA moved onto the recipe. Remaining: MAUI/WPF Hybrid sample and device runs; authentication in a sample (account switching is unit-tested only). |
| 7: Session lifecycle and notifications | **Done for web hosts** | Single-flight loop; triggers (write, interval, request, SSE hints, browser `online`/visibility); exponential backoff with jitter; `Retry-After`; Web Locks lease and follower mode; status model and per-item status; account switching; pause/resume; one-shot credential renewal; hint stream endpoint and client with reconnect. Remaining: native suspend/resume wiring in a Hybrid host (API exists), a server watermark in status, SignalR as an alternative hint transport (SSE only today). |
| 8: Conflicts, selective sync, retention | **Done (with stated gaps)** | Conservative default (`Defer`): durable unresolved conflicts in every store, list/resolve/discard in the engine and `ISyncCollection`, conflict UI in the samples' `NotesPanel`; field-level three-way merge and a merging handler; scope fingerprints in checkpoints with reset reasons (`epoch`, `scope-changed`, `expired`), device-side removal on revoke and return on regrant; tombstone retention horizon with `base-expired`; receipt expiry without double application; SQLite and IndexedDB schema 2 migrations keeping pending work; the default policy in the randomized convergence suite. Conflict UI verified end to end in three browsers (two devices, keep mine). Gaps: dependency groups (I19), semantic merges for counters/sets, retention on a durable authority (Phase 4). |
| 9: Migrations and disaster recovery | **Done for clients (with stated gaps)** | Store schema migrations (SQLite, IndexedDB 1→2 keeping pending work); rolling domain-schema upgrade over HTTP; parked rejections listed, retried as new operations or reverted (engine and `ISyncCollection`, sample UI); export/import of local work preserving operation ids; SQLite check and rebuild that moves the damaged file aside and salvages readable local work page by page; operations runbook (`docs/operations/disaster-recovery.md`, ADR-013). Gaps: no document upcasting hook; authority backup/restore exists only for the in-memory authority; storage-level faults (torn writes, power loss) not exercised. |
| 10: Operational quality and release | **Done except CI execution** | Traces and metrics in the core (`BlazorSync` source and meter: operations by outcome, conflicts, resets, run duration, queue depth and oldest pending age), session and server logs, server request/outcome metrics, a retryable 503 for authority failures, and a catch-all in the session loop; BenchmarkDotNet workloads with a recorded run (docs/benchmarks.md); package metadata and a local pack of the seven packages (not published); public API baselines checked by tests; a GitHub Actions workflow (written, **not executed**: no runner here). Gaps: no OpenTelemetry exporter test, no multi-process server benchmarks (needs the database authority), benchmarks recorded on one development machine only. |
| 11: Evidence-selected extensions | Not started | |

## Handoff: next work items, in order

1. **Run CI** on GitHub (`.github/workflows/ci.yml`) and fix what Linux/macOS runners reveal (process-kill tests
   and SQLite have only run on Windows).
2. **Phase 4 (database)** — PostgreSQL authority, when a PostgreSQL instance is available, including
   tombstone/receipt retention jobs and scope fingerprints.
3. **Phase 8 and 9 leftovers** — document upcasting hook; dependency groups for documents that must be applied
   together.
4. **Hybrid hosts** — MAUI/WPF sample with SQLite when the workloads are installed.
5. **Performance** — paged/indexed queries (`QueryAsync` materializes the collection) and fewer JSON clones on
   the sync path (see docs/benchmarks.md).
6. **Phase 11** — extensions only on evidence from real use (ADR-001): nothing selected yet.

Watch item: one run of `BlazorIntegrationTests` (out of about 80 runs on 2026-09-28) failed with a 9-second wait
timeout; it did not reproduce in 76 further runs. If it recurs, capture the failing test name and status history.

## Known limitations today

- The only authority is in memory and loses everything on restart; no PostgreSQL authority yet.
- SQLite is verified on Windows only; IndexedDB in Playwright's Chromium, Firefox and WebKit builds on Windows.
- Full-document replacement drops fields unknown to an older writer unless the document declares
  `[JsonExtensionData]` (I17 partial).
- With the default conflict policy, replicas converge only after kept conflicts are resolved or discarded;
  an app must surface them.
- Receipt retention is manual (`PurgeReceipts`); purging too early turns a replayed success into a conflict.
- The original demo (`BlazorSync.Demo`) is a single-tab simulation; the notes sample is the realistic one.
- Queries load the whole collection into memory (tens of milliseconds at 10,000 documents).
- The CI workflow has not been run; everything was verified on one Windows machine.
