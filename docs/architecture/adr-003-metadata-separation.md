# ADR-003: Separation of replication metadata

- **Status:** Accepted (2026-09-27)
- **Invariants:** I02, I04, I06, I09, I12

## Context

In the prototype, `ISyncEntity.UpdatedAt` (an HLC re-stamped by the server) served as concurrency
token, feed cursor, conflict-policy input and change detector ("edited since push" was
`UpdatedAt` inequality). Each role has different requirements, and the overlap caused baseline defects
S02, S05 and S10.

## Decision

Five separate pieces of metadata:

| Metadata | Owner | Where | Purpose |
|---|---|---|---|
| **Local revision** (`SyncRecord.LocalRevision`, `long`) | Replica | Local store only | Increments on every local write; decides whether an acknowledgement still describes the latest edit. |
| **Operation id** (`PendingOperation.OperationId`, GUIDv7 "N") | Replica | Local store + wire + server receipts | Identifies one immutable logical write for deduplication. |
| **Server document version** (`BaseVersion`, `RemoteChange.Version`, `long ≥ 1`) | Server | Wire + local store | Per-document concurrency token. Strictly increasing per document. |
| **Feed checkpoint** (`Checkpoint`, opaque string) | Server | Wire + local store | Resume position in the change feed; carries epoch. Clients never parse it. |
| **Origin HLC** (`ISyncEntity.UpdatedAt`) | Authoring replica | Document | When/where the state was authored; input to time-based policies only. The server validates its skew but never re-stamps it. |

`ISyncEntity` keeps its three members so existing entities compile. Replication metadata lives in the
`SyncRecord` envelope, not on the entity.

## Alternatives considered

- Keep the server-stamped HLC as version: rejected (S10, S05).
- Put version fields on `ISyncEntity`: rejected; it leaks protocol state into domain models and makes
  every entity mutable by the protocol.
- Content hashes as versions: rejected for v1; ABA-safe but expensive and not ordered.

## Consequences

- JSON versions are 64-bit integers. The wire specification must encode them as strings where JavaScript
  may parse them (ADR-011, Phase 2 wire fixtures).
- The in-memory reference authority uses one global commit sequence for both document versions and feed
  positions. A database provider may use separate sequences as long as per-document versions increase.

## Migration impact

`SyncRecord` gained init-only properties; its positional constructor is unchanged. Stored data from the
prototype has no versions; there is no durable store to migrate yet.

## Tests

`S02`, `S02b`, `S05`, `S10`, `OlderVersionIgnored`, `ReplayedAcknowledgementAdoptsNewerObservedVersion`.
