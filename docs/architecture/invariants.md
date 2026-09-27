# Protocol and persistence invariants

Stable identifiers for the guarantees BlazorSync is built to provide. Tests name the invariants they
check (`I02`, `T06`, …) in their display names, so `dotnet test --filter "DisplayName~I04"` selects them.

**Status** is one of:

- **Enforced (reference)**: implemented and tested with the in-memory store and in-memory authority.
  Durable providers must pass the same tests before they can claim it.
- **Partial**: some of the invariant is implemented; the gap is stated.
- **Open**: not implemented yet. The roadmap phase that owns it is given.

| ID | Invariant | Status | Where it is enforced / tested |
|---|---|---|---|
| I01 | Durable local acceptance: a successful local write and its pending intent survive supported crashes together. Memory-only stores advertise weaker durability. | Partial | The write and its queue entry are one atomic store update. `InMemoryLocalStore` is explicitly not durable. Durable stores: Phase 3 (SQLite), Phase 5 (IndexedDB). |
| I02 | Local edit preservation: acknowledging operation N never clears or overwrites a later edit. | Enforced (reference) | `SyncRecord.LocalRevision`, `PendingOperation.Revision`, `ILocalStore.UpdateAsync`. Tests: `S02`, `S02b`, `EditDuringConflictResolution`, `LocalWritesStayResponsiveDuringPush`, `StoreSnapshotsDoNotAlias`. |
| I03 | Atomic remote apply: page state and checkpoint commit together; never advance past unrepresented data. | Enforced (reference) | Pull page + checkpoint in one `UpdateAsync`; `SyncRecord.Observed` keeps server state skipped for dirty records. Tests: `S03`, `CrashDuringPageApplyIsAtomic`, `CheckpointCommittedWithPage`, `ReplayedAcknowledgementAdoptsNewerObservedVersion`. UI observation boundary: Phase 6. |
| I04 | Retry-safe operations: retries reuse id and payload; duplicates replay the original result; reused id with a different request fails. | Enforced (reference) | Persisted `PendingOperation`; `InMemorySyncServer` receipts with request fingerprints. Tests: `S04`, `DuplicateDeliveryReplaysOutcome`, `ReusedOperationIdWithDifferentPayloadFails`, `CrashBeforeSend`, `CrashBeforeLocalAcknowledgement`. Receipt retention vs replica expiry: Phase 8. Durable authority: Phase 4. |
| I05 | Atomic server concurrency check: base-version compare and write are one operation. | Enforced (reference) | In-memory authority under one lock. Test: `SameBaseSecondWriterConflicts`. Relational provider and multi-instance proof: Phase 4. |
| I06 | Safe feed progress: a cursor covers a committed, gap-free prefix in an explicit epoch. | Partial | In-memory authority: single commit sequence under one lock; epoch in the checkpoint; `EpochMismatch`. Database algorithm (delayed commits, rollback gaps): Phase 4, ADR-005. |
| I07 | Scoped isolation across tenant/principal/collection/filter/replica. | Open | Phase 4 (authorization), Phase 8 (scopes). The in-memory authority has no identity model. |
| I08 | Bounded work: payloads, pages, retries and per-cycle work are bounded; exhaustion reports resumable remaining work. | Partial | `SyncOptions` budgets (validated), server `MaxOperationsPerPush`/`MaxPageSize`, `SyncResult.HasRemainingWork/Deferred`. Tests: `S01`, `S09`, `Batching`, `PageBudget`, `ConflictBudget`, `OversizedPushRefused`. Byte limits and backoff: Phases 4 and 7. |
| I09 | Monotone reconciliation: no regression of confirmed versions or cursors; unknown/duplicate/omitted/contradictory outcomes never mark unsent data clean. | Enforced (reference) | `CorrelateOutcomes`, `ValidatePullPage`, `KnownVersion` guard. Tests: `UnknownOutcomeIsProtocolError`, `DuplicatedOutcomeIsProtocolError`, `MismatchedDocumentIsProtocolError`, `OmittedOutcomeIsDeferred`, `StaleResponseIsIgnored`, `OlderVersionIgnored`, `NonAdvancingPageRejected`, `DuplicateDocumentInPageRejected`. |
| I10 | Honest deletes and scope removals: tombstones propagate; eviction/revocation remove projections without global deletion; purge is distinguishable. | Partial | Tombstones propagate (`TombstonesPropagate`). Eviction, revocation, purge: Phase 8. |
| I11 | Deterministic conflict behaviour, no repeated side effects, preserved unresolved forks. | Partial | Handler never re-runs for a replayed outcome (`S04`); LWW is upload-order independent (`S10`); handler contract documented as pure. Durable unresolved conflicts and field-level three-way merge: Phase 8. |
| I12 | Identity/clock integrity: bounded, validated HLC; defined overflow and restart; origin time separate from version/cursor; database ordering matches comparison. | Enforced (reference) | `HlcTimestamp` validation and strict parser, bounded counter with carry, high-water seeding, server skew bound. Tests: `S05`–`S08`, `ClockValidationTests`. Replica identity/incarnation for clones: Phase 3. |
| I13 | Repair after notification loss. | Open (vacuous today) | No live notifications are consumed; every sync is a checkpoint pull. Phase 7. |
| I14 | Safe recovery/reset preserving pending edits. | Open | Epoch mismatch is detected and raised as `SyncResetRequiredException`; the reset/rebase flow is Phase 8/9. |
| I15 | Cancellation and lifecycle: no partial logical writes; post-commit cancellation is an unknown outcome. | Partial | `CancelledWriteCommitsNothing`, `CancellationAfterServerCommit`. Disposal and owned background work: Phases 6 and 7. |
| I16 | Truthful confirmation: local commit vs pending vs accepted vs rejected vs observed. | Partial | `LocalWriteReceipt` (local commit only), `SyncRecord.IsDirty/Pending/Rejection`, `SyncResult.IsComplete`. Watermarks and per-item status API: Phases 6–7. |
| I17 | Schema compatibility; old clients cannot erase unknown fields. | Open | Phase 9. Full-document replacement currently drops fields unknown to the writer. |
| I18 | Host equivalence of authorization and business rules. | Open | Phases 4 and 6. |
| I19 | Fair progress: rejected or repeatedly conflicting records do not block independent work. | Enforced (reference) | Rejected records are parked; per-document conflict budget; per-run exclusion. Tests: `MixedOutcomes`, `RejectedRecordDoesNotStarveQueue`, `ConflictBudget`. Dependency groups: Phase 8. |
| I20 | Eventual convergence under stated assumptions after quiescence. | Partial | `RandomizedConvergenceTests` (45 seeded schedules: three policies, lost responses, crashes before acknowledgement). Assumptions: one available authority, a single collection without scopes, no schema change, unbounded receipt retention, conflict handlers that terminate. |

## Assumptions behind I20 today

Convergence is tested only for the in-memory authority and store, a single collection, whole-document
last-writer or policy-based resolution, and a finite schedule followed by reliable connectivity. It does not
cover multiple scopes, revocation, receipt expiry, server restore, schema changes, or clocks that are
more than the server's skew bound ahead. Passing a finite randomized suite is evidence, not proof.
