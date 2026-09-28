# Support matrix

Tiers (ADR-012): **Verified** = executed tests or a recorded run; **Build-only** = compiles or publishes,
not executed; **Planned** = not implemented. Nothing here is Verified unless the evidence column says how.

Last updated: 2026-09-28, Windows 10 x64, .NET SDK 10.0.401, `wasm-tools` 10.0.111.

## Components

| Component | Tier | Evidence |
|---|---|---|
| Core engine, HLC, conflict handlers, three-way merge (`BlazorSync`) | Verified (in-process) | Part of 464 tests in `BlazorSync.Tests` (unit, regression, conformance, wire-fixture, fault-injection, process-kill, HTTP, Blazor and 120 seeded randomized schedules over four conflict policies); `dotnet test src/BlazorSync.Tests -c Release`. Library builds with `IsAotCompatible` and zero warnings. |
| `InMemoryLocalStore` | Verified (in-process), **not durable** | Passes `LocalStoreConformanceTests`. Loses all data when the process ends. |
| `InMemorySyncServer` reference authority | Verified (in-process), **not durable** | Passes `AuthorityConformanceTests` directly, through the JSON wire encoding and over HTTP; scopes, `CanRead`/`CanWrite`, scope fingerprints, tombstone and receipt retention. For tests and samples only. |
| Wire JSON encoding (`docs/protocol/v1.md`) | Verified (fixtures) | Valid fixtures round-trip, invalid fixtures are refused, engine traffic converges through the encoding and over the HTTP binding. |
| `InProcessTransport` | Verified (in-process) | Same suite. |
| `SqliteLocalStore` (`BlazorSync.Storage.Sqlite`) | Verified on Windows x64 only | Passes `LocalStoreConformanceTests` (single instance and two instances on one file), restart, schema (newer refused; 1→2 migration keeping a pending operation), identity and collection tests, kept conflicts across restart, check and rebuild of damaged files (header overwritten, damaged pages, damaged row), an engine convergence run with restores, and process-kill tests (child process terminated mid-write; every acknowledged write present, pages atomic with their checkpoint, `integrity_check` ok). Not run on Linux, macOS, Android or iOS; power loss not tested. |
| `IndexedDbLocalStore` (`BlazorSync.Storage.IndexedDb`) | Verified in Playwright browsers | Shared conformance cases (including kept conflicts and purge) plus multi-tab, lease, upgrade from another tab, schema 1→2 migration with pending work, unavailable-storage, deleted-database and reset tests in Playwright 1.63's Chromium, Firefox and WebKit builds on Windows (Chromium 153.0.8010.12, Firefox 155.0, WebKit 26.6); 68 browser tests passed on 2026-09-28, both with the interpreted and with the WebAssembly AOT build of the test harness (`-p:BrowserHostAot=true`). Not run in native Safari, on iOS/Android, or under real quota pressure. |
| `BlazorSync.Server.AspNetCore` endpoints + `BlazorSync.Transport.Http` client | Verified (in-process `TestServer`) | `AuthorityConformanceTests` over HTTP; header, content-type, size and operation limits; authentication, tenant isolation, read authorization; error classification with `Retry-After`; timeout after commit retried without a second effect; engines converging over HTTP through a restore. Not yet run against Kestrel over a real network or from a browser. |
| Relational authority (PostgreSQL) | Planned | Phase 4. PostgreSQL was not available on the machine used for this change. |
| `BlazorSync.Blazor` (collection API, session loop, server-connected collection, recipes) | Verified (unit + browser) | 29 integration tests with fake time (kept conflicts, retry/revert, logging, recovery from unexpected failures); exercised by both samples' browser tests, including a two-device conflict shown and resolved in the notes PWA (`NotesSampleTests.ConflictIsShownAndResolved`, all three engines). |
| Observability (traces, metrics, logs) | Verified (in-process) | `DiagnosticsTests`, `ServerObservabilityTests`, session logging tests with `MeterListener`/`ActivityListener`/test loggers. No OpenTelemetry exporter or collector exercised. |
| Packages (7 libraries) | Build-only | `dotnet pack` produced `0.1.0-preview` packages and symbol packages locally; dependency graph checked (core has no dependencies; browser packages pull neither SQLite nor ASP.NET Core). Not published, not consumed from a feed. |
| CI (`.github/workflows/ci.yml`) | Planned (written, not executed) | No GitHub runner was available; the workflow has never run. |
| Benchmarks | Recorded run | docs/benchmarks.md: one development machine, short runs. |
| Live notifications | Verified (in-process, TestServer, browsers) | In-process commit hints for server-connected circuits; Server-Sent Events hint stream (`GET …/hints`) consumed by browser sessions; dropping every hint still converges on the interval. No SignalR transport. |

## Hosts

| Host | Tier | Evidence / notes |
|---|---|---|
| Native/headless .NET 10 (tests) | Verified on Windows x64 | Test suite runs the engine without a renderer, with in-memory and SQLite stores. |
| Standalone Blazor WebAssembly (demo) | Build-only | `dotnet publish src/BlazorSync.Demo -c Release` succeeded; `-p:RunAOTCompilation=true` also succeeded with no warnings. The published app was not run in a browser in this change. The demo simulates devices in one tab against an in-process server; it is not a persistent multi-device deployment. |
| WASM PWA (notes sample) | Verified with gaps | Published sample server + client in Playwright: offline reload with notes and upload on reconnect (Chromium, Firefox; **WebKit unverified**, Playwright WebKit fails on offline service-worker navigation), service-worker update that waits for the user and keeps notes (all three), server restart with in-memory data loss (all three). |
| Interactive Server, Interactive WebAssembly, Auto, static SSR (Blazor Web App sample) | Verified in Playwright browsers | Published sample server process, Chromium/Firefox/WebKit: static SSR and prerender without JavaScript, Server live updates and circuit recreation, WebAssembly writes reaching Server users, Auto on the server first and in WebAssembly later, prerender never overwriting unsynced local edits. |
| MAUI Hybrid, WPF/WinForms Hybrid | Build-only pieces, no sample | `AddLocalSyncCollection` with `SqliteLocalStore` is the intended recipe; the session loop is unit-tested headless. No Hybrid sample or device run: no MAUI workloads or runners on this machine. |
