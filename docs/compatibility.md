# Compatibility policy and migration notes

## Policy

- BlazorSync is pre-1.0 and has not been published as a package. Minor versions may contain breaking
  changes. Every breaking change is listed below with a migration path.
- From 1.0: semantic versioning; public API compatibility checked in CI; wire protocol, store schema and
  domain schema versioned independently (ADR-011); a documented client/server compatibility window.
- Behavioural changes count as breaking even when signatures do not change.

## Unreleased (Phase 2: wire encoding, conformance, AOT)

| Change | Why | Migration |
|---|---|---|
| `HlcTimestamp` serializes to JSON as its canonical string (`"001790000000000:000000:node"`) instead of an object; malformed strings are rejected. | Wire spec §2.3; sortable, validated. | Data serialized by earlier builds with the object form no longer deserializes. No durable store existed, so no stored data is affected. |
| `Checkpoint` serializes as a string or `null`. | Wire spec §2.5. | None. |
| Protocol records carry explicit JSON names; versions are digit strings; `PushOutcomeKind` is a kebab-case string (`retry-later`); integers and unknown kinds are refused. | Wire spec §3. | Use the protocol types with a source-generated `JsonSerializerContext`. |
| `SyncOptions.Cloner` removed. The engine has a reflection constructor (annotated `[RequiresUnreferencedCode]`) and a new constructor taking `Func<T, T> cloner`. | ADR-011: no hidden reflection. | `new SyncEngine<T>(store, transport, clock, cloner, handler, options)`; for JSON use `DocumentCloner.Json(context.T)`. |
| `InMemoryLocalStore()` (reflection) and `InMemoryLocalStore(Func<T,T>)` are separate constructors; the parameterless one is annotated. | Same. | Pass a cloner in trimmed apps. |
| `InMemorySyncServer(string, Func<T,T>?)` replaced by an annotated `InMemorySyncServer(string serverId = "server")`; `InMemorySyncServerOptions.Cloner` and `Fingerprint` are `required`. | Same; the demo's Conflict Lab was silently using reflection fingerprints. | Use the options constructor with `DocumentCloner.Json`/`JsonFingerprint`. |
| `DocumentCloner.Json(JsonTypeInfo<T>)` and `DocumentCloner.JsonFingerprint(JsonTypeInfo<T>)` added; `BlazorSync` is marked `IsAotCompatible`. | AOT-safe helpers. | Additive. |
| `InMemoryLocalStore.UpdateAsync` returns a fresh copy of the committed record for unchanged entries (previously the transform's working copy). | Found by the store conformance suite. | None. |

## Phase 1 and first part of Phase 2

### Behaviour

| Change | Why | Migration |
|---|---|---|
| The server no longer re-stamps `UpdatedAt`; it is the authoring (origin) timestamp. | Fixes upload-order-dependent LWW (S10) and clock poisoning (S05). | Do not use `UpdatedAt` as a server change cursor or version. Use `SyncRecord.BaseVersion` for the confirmed server version. |
| The server rejects writes whose `UpdatedAt` is more than `MaxClockSkew` (default 5 min) ahead of server time. | I12. | Rejected records carry `SyncRecord.Rejection` with code `clock-skew`; fix the device clock and write again. |
| `WriteAsync` no longer mutates the caller's document. | I02: no hidden aliasing. | Read the stamped timestamp from the returned `LocalWriteReceipt`. |
| `LastWriteWinsConflictHandler` returns `ConflictOutcome.KeepFork` (the fork keeps its origin timestamp) instead of `Resolve(fork)`. | Upload-order independence. | Only affects code that inspected the handler's result. |
| Replication calls on one engine are single-flight; overlapping calls wait. | T10. | None. |
| `PushAsync` drains the whole queue in batches, bounded by `MaxPushBatches`, instead of one batch. | S01. | None; check `SyncResult.IsComplete`. |
| Push results that name unknown operations, repeat an operation, or carry malformed state throw `SyncProtocolException` before anything is applied. | I09. | Custom transports must return one outcome per operation id. |
| Options are validated; out-of-range values throw `ArgumentOutOfRangeException` from the engine constructor. | S09. | Fix the configuration. |
| Document ids must satisfy `SyncIds.IsValid` (1–256 UTF-16 code units, no control characters or unpaired surrogates). | T59. | Validate ids at creation. |
| HLC node ids must be 1–64 characters from `[A-Za-z0-9._~-]`; `HlcTimestamp` validates its fields; `Parse` is strict. | S06, S07, I12. | Use GUIDs in "N" format or similar for node ids. |

### API

| Change | Migration |
|---|---|
| `ILocalStore`: `UpsertAsync`, `SetCheckpointAsync`, `GetDirtyAsync` removed; `UpdateAsync`, `GetPendingAsync`, `CountDirtyAsync`, `GetClockHighWaterAsync` added. | Custom stores implement the atomic `UpdateAsync` contract (ADR-004). |
| `SyncRecord` gained `BaseVersion`, `LocalRevision`, `Pending`, `Rejection`, `Observed`, `ObservedVersion`, `IsPushable`, `KnownVersion`. | Positional constructor unchanged. |
| `Checkpoint` is now an opaque `Checkpoint(string? Value)`; `IsBefore` removed. | Treat checkpoints as opaque tokens. |
| `PushRow`/`PushRequest(Rows)`/`PushResult(Accepted, Conflicts)` replaced by `PushOperation`/`PushRequest(Operations)`/`PushResult(Outcomes)` with `PushOutcome`. | Custom transports and servers adopt the new messages (see `InMemorySyncServer`). |
| `PullResult.Documents` replaced by `PullResult.Changes` (`RemoteChange` with `Version`); `StreamEvent.Documents` likewise. | As above. |
| `SyncOptions.MaxPushPasses` removed; added `MaxPushBatches`, `MaxPullPages`, `MaxConflictRetries`, `Validate()`. | Use `MaxPushBatches` for total work and `MaxConflictRetries` for per-document conflict retries. |
| `SyncResult` gained `Rejected`, `Deferred`, `HasRemainingWork`, `IsComplete`. | Additive. |
| `SyncEngine.WriteAsync` returns `Task<LocalWriteReceipt>`; `DeleteAsync` returns `Task<LocalWriteReceipt?>`; added `CountDirtyAsync`. | Source-compatible for `await` callers; binary-incompatible. |
| `ConflictOutcome.KeepFork` and `ConflictResolution.KeepFork()` added. | Switch statements over `ConflictOutcome` need a new case. |
| `HybridLogicalClock` constructor gained optional `highWaterMark` and `maxForwardDrift`; added `Last`. `ClockDriftException` added. | Additive. |
| `InMemorySyncServer`: new options constructor (`InMemorySyncServerOptions`), `Epoch`, `GetVersion`, `ReceiptCount`. First constructor parameter renamed `node` → `serverId`. | Named-argument callers update the name. |
| Added `SyncProtocolException`, `SyncResetRequiredException`, `SyncIds`, `PushErrorCodes`. | Additive. |

### Test changes

The 18 original tests are kept. One assertion changed: `ConflictHandlerTests.LastWriteWins_PrefersTheNewerTimestamp`
now expects `KeepFork` instead of `UseResolved` for a newer fork.
