# ADR-009: Server integration and change capture

- **Status:** Proposed; implementation in Phase 4 (2026-09-27)
- **Invariants:** I04, I05, I06, I18

## Context

The feed must include every change to synchronized data, however it was written: replication pushes,
ordinary API endpoints, background jobs, admin tools, raw SQL.

## Alternatives

1. **Controlled authoritative write service.** All writes to synchronized collections go through one
   application service that performs the conditional write, records the operation receipt and appends to
   the feed in one transaction.
2. **Database triggers** populating a change table. Captures raw SQL writes; logic lives in the database.
3. **CDC** (PostgreSQL logical decoding, SQL Server change tracking, Debezium outbox). Captures
   everything in commit order; heavy operational footprint.

## Decision

- v1 uses (1). The same service is called by the HTTP endpoints and by in-process callers (Blazor Server,
  background jobs), so authorization and validation are identical (I18).
- One relational provider first: **PostgreSQL** via EF Core (`BlazorSync.Server.EntityFrameworkCore`),
  chosen for transactional DDL, `xid8`/snapshot functions usable for the feed watermark (ADR-005),
  robust unique constraints for receipts, and wide hosting availability. SQL Server is the likely second
  provider.
- Writes that bypass the service (raw SQL, bulk tools, other applications) are **unsupported** until a
  tested capture mechanism (2 or 3) exists; documentation says so explicitly.
- Correctness never relies on a process lock; concurrency is enforced by conditional `UPDATE … WHERE
  version = @base` and unique indexes on `(scope, operation_id)` and `(scope, document_id)`.

## Consequences

Phase 4 cannot be completed on a machine without PostgreSQL. The EF Core in-memory provider is not an
acceptable substitute for concurrency and ordering tests.
