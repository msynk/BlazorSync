# Baseline review

Record of the repository state before the synchronization toolkit work began, and of what changed in
Phase 0 and Phase 1 (with the first part of Phase 2).

## Reviewed commit

| Item | Value |
|---|---|
| Commit | `0083a02d07cdef122d19967a8a7f2ed5239c872a` (`main`, "project restructure") |
| Compared with the external review's baseline | Identical commit; the working tree was clean. |
| Date | 2026-09-27 |
| Machine | Windows 10 Enterprise 10.0.19045, x64 |
| .NET SDKs | 10.0.302, 10.0.401, 10.0.400-preview |
| Workloads | `wasm-tools` (10.0.111) |
| Other tools | Node.js 26.1.0. **Not available:** Docker, PostgreSQL, SQLite CLI, Playwright browsers, Android/iOS/macOS toolchains. |

## Layout at the reviewed commit

- Solution: `src/Bsync.slnx`.
- Projects: `src/Bsync` (library, `net10.0`, no package dependencies, warnings as errors),
  `src/Bsync.Tests` (xUnit), `src/Bsync.Demo` (standalone Blazor WebAssembly).
- The README described `samples/Bsync.Demo` and `tests/Bsync.Tests`, which do not exist on
  disk; `dotnet run --project samples/Bsync.Demo` failed. Fixed in this change.

## Commands and results at the reviewed commit

Run from `src/`:

```bash
dotnet build Bsync.slnx -c Release          # succeeded, 0 warnings
dotnet test Bsync.slnx -c Release --no-build # 18 passed (6 clock, 4 conflict, 8 engine)
dotnet publish Bsync.Demo -c Release -o <dir> # succeeded (WASM relink with wasm-tools)
```

Not verified at the baseline: explicit WASM AOT publish, running the published app in a browser.

## Characterization of the ten reported scenarios

The ten scenarios were recreated as *desired-behaviour* tests against the reviewed API. All ten failed
on `0083a02`. The source is kept, uncompiled, at
[`characterization/BaselineCharacterizationTests.cs.txt`](characterization/BaselineCharacterizationTests.cs.txt).

To reproduce: `git checkout 0083a02`, copy that file to `src/Bsync.Tests/BaselineCharacterizationTests.cs`,
and run `dotnet test src/Bsync.Tests -c Release --filter FullyQualifiedName~BaselineCharacterization`.

Deterministic interleavings are injected with a store decorator that runs a local edit between the
engine's read and its write of the same record, and a transport decorator that drops a response after the
server committed. S10 is the only test that depends on real time (the reviewed server had no injectable
clock); it sleeps 20 ms so that server re-stamping happens after both authoring times.

| # | Scenario | Result on 0083a02 | Classification | Invariant | Status after this change |
|---|---|---|---|---|---|
| S01 | 5 dirty records, batch size 2: push sent 2 | Failed (2 ≠ 5) | Verified defect | I08 | Fixed; `BaselineRegressionTests.S01`, `PushProtocolTests.Batching` |
| S02 | Edit between reading and writing the acknowledged record is overwritten and marked clean | Failed | Verified defect | I02 | Fixed by atomic compare-and-transform with local revisions; `S02`, `S02b` |
| S03 | Edit between reading a clean record and applying a pulled replacement is lost | Failed | Verified defect | I03 | Fixed; pull pages commit atomically with the checkpoint; `S03` |
| S04 | Lost push response + retry re-runs conflict resolution (`one` → `one\|one`) | Failed | Verified defect (missing operation identity) | I04 | Fixed in the reference authority: persisted operation ids and server receipts; `S04` |
| S05 | Far-future client HLC advanced the server clock for later writes | Failed | Verified defect | I12 | Fixed: server no longer adopts client clocks; bounded skew rejection; `S05` |
| S06 | `Update` with `int.MaxValue` counter overflowed to a negative counter | Failed | Verified defect | I12 | Fixed: counter bounded to 999,999, overflow carries into wall time; `S06` |
| S07 | Counter 1,000,000 broke encoded-string ordering | Failed | Verified defect | I12 | Fixed: construction validates every field; `S07`, `ClockValidationTests` |
| S08 | Restarted clock with same node and frozen time reused a timestamp | Failed | Verified defect (no high-water mark) | I12 | Fixed: store tracks a clock high-water mark; engine seeds the clock; `S08` |
| S09 | `PushBatchSize = 0` accepted, silently left all work pending | Failed | Verified defect | I08 | Fixed: options validated in the engine constructor; `S09` |
| S10 | LWW result depended on upload order because the server re-stamped accepted writes | Failed (A vs B) | Semantic ambiguity | I11, I12 | Resolved: origin timestamps are preserved; LWW keeps the winning fork's timestamp; `S10` |

## Other findings from inspection

| Finding | Classification | Status |
|---|---|---|
| `DeleteAsync` and `ResolveConflictAsync` used the same separate `GetAsync`/`UpsertAsync` pattern as S02 | Verified by inspection (same race class) | Fixed; covered by `PushProtocolTests.EditDuringConflictResolution` |
| Overlapping `SyncAsync` calls ran concurrently; per-method store locks did not make multi-call workflows atomic | Unverified risk at baseline | Fixed: single-flight replication per engine; `ConcurrencyAndRecoveryTests.OverlappingSyncsAreSingleFlight` |
| `WriteAsync` mutated the caller's document (`UpdatedAt`) | Semantic issue | Fixed: the engine stores a copy and returns a `LocalWriteReceipt` |
| The server's HLC restamp doubled as concurrency token, pull cursor and LWW input | Design ambiguity | Separated: server version (concurrency), opaque checkpoint (feed), origin HLC (policy) |
| `ClientWins` is the default conflict policy and is lossy for the remote edit | Documented behaviour | Unchanged in Phase 1; see ADR-006 and roadmap Phase 8 |
| Reflection-based JSON clone defaults are hidden behind `#pragma` suppressions | Unverified trimming risk | Unchanged; the demo now supplies explicit cloner and a source-generated fingerprint. Tracked in the roadmap (Phase 2, task 5) |
| `ISyncTransport.StreamAsync` is declared but throws | Missing feature | Unchanged; documented as not consumed |
| No HTTP endpoints, durable stores, server persistence, authorization, scheduler or live consumer | Missing features | Unchanged; roadmap Phases 3–7 |
| The demo simulates several devices in one browser tab against one in-process server | Accurate description | Unchanged; it is a teaching sample, not a deployment |

## Defect found during this change

`RandomizedConvergenceTests` (seeded schedules with lost responses and simulated crashes) failed for
multiple seeds under all three conflict policies on the first run of the new engine. Cause: while a record was dirty, a pull skipped a newer
server version but still advanced the checkpoint; when the retried push then received the *replayed*
acknowledgement of an older version, the record became clean at that older version and never saw the
newer one. Fix: the record keeps the newest server state observed while dirty (`SyncRecord.Observed`) and
adopts it when it settles. Deterministic regression:
`PushProtocolTests.ReplayedAcknowledgementAdoptsNewerObservedVersion`. All 45 randomized cases pass after
the fix.

## Results after this change

See the "Building and testing" section of the README for commands. At the end of this change:

- `dotnet build src/Bsync.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test src/Bsync.slnx -c Release`: 152 tests passed, 0 failed (the 18 original tests are
  kept; one assertion changed as documented in `docs/compatibility.md`).
- Demo publish: see the verification table in `docs/support-matrix.md`.
