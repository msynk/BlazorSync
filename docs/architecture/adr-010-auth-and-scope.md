# ADR-010: Authentication and scope identity

- **Status:** Accepted; implemented for the reference authority (2026-09-28)
- **Invariants:** I07, I10, I14, I18

## Decision

- **Authority comes from the authenticated context**, never from request bodies. Tenant and principal are
  derived server-side; any tenant id in a request is at most a consistency check.
- A **scope** is `(tenant, principal or group, collection, filter definition, schema version)`. The server
  computes a scope fingerprint; checkpoints and receipts are bound to it. A checkpoint presented under a
  different scope is rejected with a reset requirement.
- A **replica** has an installation id, an account namespace and an incarnation id. A restored backup or
  cloned device gets a new incarnation, so its clock and receipts cannot collide with the original.
- Operation receipts are keyed by `(scope, operation id)` and store a request fingerprint; conflict and
  duplicate responses are filtered with the same read authorization as the feed.
- **Revocation** removes the local projection on the next reconciliation without creating global
  deletions; regrant repopulates.
- **Logout/account switch**: the browser/native session stops, in-flight responses are discarded, and
  local data is retained only under an explicit, documented policy (encrypted at rest where the platform
  supports it).
- Browser clients use cookie authentication with CSRF protection or bearer tokens with renewal; no
  database credentials or client secrets are ever shipped to WebAssembly. Logs redact tokens and document
  bodies by default.

## Implementation status (2026-09-28)

- Scope from authenticated claims (`SyncEndpointOptions.ResolveScope`, `SyncCallContext`), isolated
  per-scope authorities (`ScopedAuthority`), per-document `CanRead`/`CanWrite`, filtered conflict and replay
  responses: implemented and tested (HTTP and in-process).
- The scope fingerprint is `InMemorySyncServerOptions.ScopeFingerprint(context)`; its hash is part of every
  checkpoint. A different fingerprint yields `reset-required` with reason `scope-changed`; the replica
  resnapshots and removes documents no longer visible (never records with local changes or kept conflicts).
  Revocation and regrant are tested (`SelectiveSyncTests`).
- Receipts are per scope (each scope has its own authority); receipts are not yet bound to a fingerprint
  inside one scope.
- Replica id and incarnation: SQLite and IndexedDB stores (Phases 3 and 5). Account switching stops the
  session and abandons in-flight work (Phase 7). Encryption at rest and token/cookie guidance with a real
  identity provider: not done.
