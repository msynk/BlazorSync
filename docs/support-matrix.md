# Support matrix

Tiers (ADR-012): **Verified** = executed tests or a recorded run; **Build-only** = compiles or publishes,
not executed; **Planned** = not implemented. Nothing here is Verified unless the evidence column says how.

Last updated: 2026-09-27, Windows 10 x64, .NET SDK 10.0.401, `wasm-tools` 10.0.111.

## Components

| Component | Tier | Evidence |
|---|---|---|
| Core engine, HLC, conflict handlers (`BlazorSync`) | Verified (in-process) | 205 unit, regression, conformance, wire-fixture, fault-injection and seeded randomized tests (`dotnet test src/BlazorSync.slnx -c Release`). Library builds with `IsAotCompatible` and zero warnings. |
| `InMemoryLocalStore` | Verified (in-process), **not durable** | Passes `LocalStoreConformanceTests`. Loses all data when the process ends. |
| `InMemorySyncServer` reference authority | Verified (in-process), **not durable, no auth** | Passes `AuthorityConformanceTests` directly and through the JSON wire encoding. For tests and samples only. |
| Wire JSON encoding (`docs/protocol/v1.md`) | Verified (fixtures) | 6 valid fixtures round-trip, 12 invalid fixtures refused, engine traffic converges through the encoding. No HTTP binding yet. |
| `InProcessTransport` | Verified (in-process) | Same suite. |
| Durable native store (SQLite) | Planned | Phase 3. |
| Browser store (IndexedDB) | Planned | Phase 5. |
| HTTP transport and ASP.NET Core endpoints | Planned | Phase 4. |
| Relational authority (PostgreSQL) | Planned | Phase 4. PostgreSQL was not available on the machine used for this change. |
| Live notifications (SignalR/SSE) | Planned | Phase 7. `ISyncTransport.StreamAsync` is declared but not consumed. |

## Hosts

| Host | Tier | Evidence / notes |
|---|---|---|
| Native/headless .NET 10 (tests) | Verified (in-process only) | Test suite runs the engine without a renderer. No durable store yet. |
| Standalone Blazor WebAssembly (demo) | Build-only | `dotnet publish src/BlazorSync.Demo -c Release` succeeded; `-p:RunAOTCompilation=true` also succeeded with no warnings. The published app was not run in a browser in this change. The demo simulates devices in one tab against an in-process server; it is not a persistent multi-device deployment. |
| WASM PWA | Planned | Phase 5. |
| Interactive WebAssembly / Server / Auto, static SSR | Planned | Phase 6. |
| MAUI Hybrid, WPF/WinForms Hybrid | Planned | Phase 6. No MAUI workloads or device runners on the machine used for this change. |
