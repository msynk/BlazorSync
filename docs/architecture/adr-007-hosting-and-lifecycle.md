# ADR-007: Hosting profiles, application API, DI lifetimes and lifecycle

- **Status:** Proposed; implementation in Phases 6 and 7 (2026-09-27)
- **Invariants:** I07, I13, I15, I16, I18

## Context

Blazor code runs in different places: in the browser (WebAssembly), on the server per circuit
(Interactive Server), per request (static SSR, prerendering), and natively (MAUI/WPF/WinForms Hybrid).
Auto render mode moves a component from server to browser across visits, not live. Offline local work is
only possible where application code executes on the device.

## Decision

Host profiles, each with explicit capabilities rather than pretended equivalence:

| Profile | Replica | Offline writes | Confirmation | Owner of sync loop |
|---|---|---|---|---|
| Browser (WASM, PWA, Interactive WASM after activation) | IndexedDB per account/scope | Yes (durable, subject to eviction) | Local commit, then server | Browser session service, one leader tab |
| Native (MAUI, WPF, WinForms, headless) | SQLite | Yes | Local commit, then server | Host-owned session service |
| Server-connected (Interactive Server, Auto's server phase) | None on device; calls authority in-process | No | Server commit is the confirmation | None; notifications only |
| Request (static SSR, prerender) | None | No | Server commit | None |

- The application-facing API is a small **collection facade** (get, bounded query, write/delete
  returning a receipt, observe) and a **session** (start, stop, reconcile now, status, dispose). Receipts
  and status carry a capability-aware confirmation level so components need not know the host.
- DI: explicit registration per profile (`AddBlazorSyncBrowser`, `AddBlazorSyncServer`, …), no runtime
  guessing. Server: circuit-scoped facade, request-scoped operations, `IDbContextFactory`, singletons only
  for stateless or partitioned infrastructure. Browser/native: session scoped to the authenticated account.
- Prerender never touches JS storage or persists replicas, credentials or cursors into HTML. A bounded
  authorized snapshot may seed the first render but never overwrites a dirty local replica.
- Lifecycle: the session owns its background work; disposal cancels it and stops UI callbacks. Account
  switch is a barrier: responses started for the previous account are discarded.

## Consequences

The core stays renderer-agnostic. Server and browser DI containers are never assumed to share memory.

## Tests (to be written)

T46–T50 (prerender, Auto first/later visit, dirty local vs snapshot, circuit recreation, disposal during
callback), T39 (account switch during flight).
