# ADR-010: Authentication and scope identity

- **Status:** Proposed; implementation in Phases 4, 6 and 8 (2026-09-27)
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
