# ADR-011: Wire, store and domain compatibility

- **Status:** Accepted in principle; fixtures in Phase 2, full policy in Phase 9 (2026-09-27)
- **Invariants:** I14, I17

## Decision

- Three independent versions: **protocol version** (wire messages and semantics), **store schema
  version** (local metadata layout per provider), **domain schema version** (the application's document
  shape).
- Clients send their protocol version and domain schema version on every request. The server answers
  unsupported combinations with an explicit upgrade-required error and never partially applies them.
- JSON encoding: 64-bit versions and positions are strings on the wire; timestamps use the canonical HLC
  encoding; ids are UTF-8 strings compared ordinally; `null` and absent are distinct. Unknown fields must be
  preserved by clients that round-trip documents (I17), which requires either field-level operations or a
  preserved raw-JSON bag; full-document replacement by an old client is otherwise lossy.
- Serialization uses System.Text.Json source generation. Reflection defaults remain available for
  non-trimmed hosts but are documented as unsupported for trimmed/AOT publishing.
- Pending operations are never rewritten after they may have been sent. A migration that cannot preserve
  an operation's identity and meaning moves it to a quarantine for explicit recovery.

## Current state

- The JSON encoding is normative in `docs/protocol/v1.md` and enforced by strict converters in
  `Bsync.Protocol` (`WireInt64JsonConverter`, `HlcTimestampJsonConverter`, `CheckpointJsonConverter`,
  `PushOutcomeKindJsonConverter`) and by valid/invalid fixtures.
- Reflection-based defaults are annotated with `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`; the
  library is `IsAotCompatible` and builds without warnings; trim-safe overloads take delegates or
  `JsonTypeInfo<T>`.
- Unknown document fields survive only for documents that declare `[JsonExtensionData]`.
- Protocol/schema version headers are specified for the HTTP binding (Phase 4) but not implemented.
