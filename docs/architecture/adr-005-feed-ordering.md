# ADR-005: Server feed ordering

- **Status:** Accepted; implemented for the reference authority and for PostgreSQL (serialized per-feed
  allocation, 2026-09-28)
- **Invariants:** I03, I06, I09, I14

## Context

The prototype paged by `(UpdatedAt, Id)` over server-stamped HLCs. With a database and concurrent
transactions, sorting by a value assigned before commit lets a slower transaction commit a lower value
after a client has already read past it, so the client never sees that change.

## Decision

- A checkpoint is an opaque server-issued string. It encodes an **epoch** (identifying the feed history)
  and a position. A checkpoint from another epoch yields `SyncResetRequiredException` (reset flow: ADR-006
  and Phase 8/9).
- A checkpoint MUST denote a **committed, gap-free prefix**: every change that will ever be visible with a
  position ≤ the checkpoint is already committed when the checkpoint is issued.
- A page contains at most one change per document (the latest), in feed order. `HasMore = true` requires
  the checkpoint to advance; clients treat violations as protocol errors.
- **Reference authority:** all operations run under one lock and the position is a single commit
  sequence, so visibility order equals sequence order.

## Proposed database algorithm (Phase 4, PostgreSQL)

Preferred: a transactional change log table written in the same transaction as the document write, with
positions assigned so that a reader never skips a later-committing lower position. Candidates to evaluate
and test:

1. **Visibility watermark via `pg_snapshot_xmin`.** Store `xid8` of the writing transaction; readers only
   return rows whose transaction is below the current snapshot's `xmin`, and the checkpoint records that
   bound. Does not serialize writers; long-running transactions hold the watermark back.
2. **Serialized sequence allocation** (one row lock per collection per commit). Simple, correct, limits
   write throughput.
3. **Logical decoding / CDC** (commit order from the WAL). Strongest ordering, more operational burden.

Required tests before claiming I06: delayed lower-sequence commit (T27), rollback gap (T28), multiple
application instances (T30), server restart.

## PostgreSQL implementation (2026-09-28)

Candidate 2 (serialized allocation) was chosen for v1, per feed (collection and scope), not per collection:

- A push transaction locks its feed row with `SELECT … FOR UPDATE` and assigns versions from it.
- It writes documents, receipts and the new sequence in the same transaction.
- A second writer of the same feed waits for the lock. So a version can only become visible after every lower
  version of that feed has committed or rolled back, and a rolled-back version is reused, never skipped.
- Writers of different feeds do not wait for each other.

Evidence (`Bsync.Tests.PostgreSql`, PostgreSQL 17.6):
- `DelayedCommitIsNeverSkipped`: T27 and T28 with a real uncommitted transaction.
- `ConcurrentInstancesKeepCommittedPrefix`: T30, with 12 writers on two instances and a continuous reader.
  Removing the lock makes this test fail.
- `SurvivesRestart`, and the shared conformance suite.

Cost: one writer at a time per feed. Candidate 1 (`pg_snapshot_xmin` watermark) remains the option if
per-feed write throughput becomes the bottleneck. No throughput measurement against PostgreSQL exists yet.

## Restores and epochs (implemented, 2026-09-27)

- An authority whose history may have been lost starts a new epoch and continues its version sequence
  above anything the lost history may have issued (`InMemorySyncServerOptions.VersionFloor`, restore via
  `CreateBackup`/`RestoreFrom`). Receipts in the backup are kept.
- Replicas reset with a generation counter and a resnapshot (`docs/protocol/v1.md` §6.1). Without the
  version rule, a pending operation's base version could match a different state after a restore and
  overwrite it silently; `ResetTests.EditOnLostVersionConflicts` covers this.

## Retention

Tombstones and receipts are purged explicitly (`PurgeTombstones`/`PurgeTombstonesAsync`,
`PurgeReceipts`/`PurgeReceiptsAsync`). Purging tombstones raises a retention horizon: older checkpoints get
`reset-required` with reason `expired`, never a silent skip (protocol §6.1).

## Tests

`PullProtocolTests` (paging, budget, atomic apply, malformed pages, epoch mismatch).
