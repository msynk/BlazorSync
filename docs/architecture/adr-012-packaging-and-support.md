# ADR-012: Packaging, supported platforms, support tiers and workloads

- **Status:** Accepted (2026-09-27)

## Decision

- **Target framework:** `net10.0` only. .NET 10 is the current LTS release (see ADR-001 sources for the
  lifecycle dates checked). Adding older targets requires a demonstrated user need and CI coverage.
- **Packages are split only when a real boundary exists** (a dependency that must not flow into another
  host). Planned order: `Bsync` (core, dependency-free) → `Bsync.Storage.Sqlite` →
  `Bsync.Server` + `Bsync.Server.AspNetCore` + `Bsync.Server.EntityFrameworkCore` →
  `Bsync.Transport.Http` → `Bsync.Storage.IndexedDb` → `Bsync.Client` → `Bsync.Blazor` → `Bsync.Testing`.
  `Bsync.Client` (collection API, local session, DI recipe) is split from `Bsync.Blazor` because
  `Microsoft.AspNetCore.Components` must not flow into native hosts without Blazor; `Bsync.Blazor` keeps only
  the server-connected profile, which needs the Blazor authentication state.
  Native database binaries must never enter the WebAssembly dependency graph; server code must never
  enter the client bundle.
- **Support tiers** published in `docs/support-matrix.md`:
  - *Verified*: executed smoke and recovery tests in CI or a recorded manual run with versions.
  - *Build-only*: compiles/publishes; not executed.
  - *Planned*: not implemented.
  No host is described as supported without *Verified* evidence.
- **Public API:** pre-1.0 releases may break with migration notes (`docs/compatibility.md`). From 1.0,
  API compatibility is checked in CI (package validation baseline).
- **Releases:** publishing packages is a separate, explicitly authorized action and is not performed by
  development work.

## Provisional workloads (targets to calibrate, not measurements)

| Dimension | Workload |
|---|---|
| Functional | 10,000 documents of ~1 KiB, 10% updates and tombstones |
| Scale | 100,000 documents, large backlog, skewed hot keys, bounded pages |
| Local UX | p95 durable write and small indexed query < 50 ms on a named reference device |
| Reconnect | 10,000 queued writes converge without manual repair or duplicate effects |
| Concurrency | multiple server processes; 100, then 1,000 simulated sessions |
| Memory | steady-state sync memory proportional to batch/window size |

Supported offline duration and document size limits will be set together with retention (Phase 8); until
then there is no retention limit and no size limit beyond the batch budgets.
