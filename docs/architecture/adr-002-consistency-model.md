# ADR-002: Consistency model and scope

- **Status:** Accepted (2026-09-27)
- **Invariants:** I02, I03, I05, I09, I16, I20

## Context

The prototype did not state what it guaranteed. The server's re-stamped HLC was simultaneously the
concurrency token, the pull cursor and the LWW input, which made LWW results depend on upload order
(baseline S10) and made "synced" undefined.

## Alternatives

1. **Globally serializable disconnected writes.** Impossible without coordination while offline.
2. **Causal consistency across documents.** Requires tracking dependencies between documents and
   delivering them in causal order; an HLC alone does not provide this.
3. **Per-document optimistic concurrency with an authoritative server, eventual convergence.** Chosen.

## Decision

Bsync guarantees, and only guarantees:

- **Atomic local writes.** A local write of one document commits (state + pending intent) atomically.
- **Per-document server concurrency.** The server accepts an operation only if its base version equals
  the document's current version (compare-and-set). Different documents are independent.
- **Independent batch semantics.** A push request is a set of independent operations. Partial acceptance
  is normal. There are no multi-document transactions in v1 (see "Deferred").
- **Committed-prefix feed.** A checkpoint covers every change committed before it (ADR-005).
- **Resumable eventual convergence** under the assumptions listed in `invariants.md` (I20).
- **Truthful confirmation levels:** *local commit* (`LocalWriteReceipt`), *pending*
  (`SyncRecord.IsDirty`), *server accepted* (record clean at a server version), *rejected*
  (`SyncRecord.Rejection`). A `SyncResult` with `IsComplete` means only that, at that moment, the queue
  was drained and the pull reached the checkpoint the server reported as current.

Not guaranteed: cross-document ordering on other replicas, read-your-writes across replicas before a
pull, causal consistency, bounded staleness.

## Consequences

- Applications needing invariants across documents must model them inside one document or wait for
  transaction groups.
- The HLC is origin metadata only (ADR-003).

## Deferred

Atomic transaction groups (all-or-nothing across documents) and dependency ordering between operations.
They require the durable unit of acknowledgement to equal the transactional unit and are planned with
Phase 8 fairness work.

## Migration impact

`SyncResult` gained `Rejected`, `Deferred`, `HasRemainingWork`, `IsComplete`. Callers that treated a
normal return as "fully synced" should check `IsComplete`.

## Tests

`BaselineRegressionTests.S10`, `PushProtocolTests.MixedOutcomes`, `SameBaseSecondWriterConflicts`,
`RandomizedConvergenceTests`.
