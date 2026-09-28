# ADR-007: Hosting profiles, application API, DI lifetimes and lifecycle

- **Status:** Accepted; implemented for every profile. The browser, server-connected and request profiles are
  covered by `BlazorSync.Blazor`. The native profile has WPF and .NET MAUI (Windows) Blazor Hybrid samples, each
  with SQLite, HTTP, pause/resume on minimize/background, and an automated UI test (2026-09-28).
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

## Implementation (2026-09-28)

- `ISyncCollection<T>`: get, bounded in-memory query (`SyncQuery`: filter, order, limit ≤ 1000),
  save/delete returning `SyncWriteResult` (`SavedLocally`, `AcceptedByServer`, `Conflict`, `Rejected`,
  `NotFound`), `Subscribe` (disposable), `Status`, `Capabilities`.
- `LocalSyncCollection<T>` over `SyncSession<T>`: lazily opens the replica for the resolved account (never
  during prerendering: the server's container never contains it); switching account stops the previous
  loop and waits for it before the next replica opens. The session loop syncs on request, on an interval
  and after local writes; exponential backoff with jitter; `Retry-After` is not shortened by requests;
  follower (no lease) never replicates; status `Starting`, `Synced`, `Syncing`, `Offline`, `Follower`,
  `AttentionRequired`, `Stopped`.
- `ServerSyncCollection<T>` (scoped per circuit/request): in-process reads/writes as the authenticated user
  (`AuthenticationStateProvider`); writes use the version the instance last read (an unseen document is
  written as new, so it cannot be silently overwritten); conflicts surface immediately; commit hints from the
  authority refresh other circuits in the same scope; queries refuse to scan beyond 10,000 documents.
- Recipes: `AddServerSyncCollection` (server), `AddLocalSyncCollection` (native/any local store; refuses
  to register in an ASP.NET Core container), `AddBrowserSyncCollection` (IndexedDB + Web Locks lease).
- Prerendered HTML comes from the server collection; after activation the WebAssembly component shows the
  local replica. Nothing from the prerendered state is imported into the replica, so a dirty replica can
  never be overwritten by it (T48).

## Tests

Unit: `BlazorIntegrationTests` (16, fake time): local save → upload → `Synced`; backoff growth and triggers;
`Retry-After`; follower; account switch with an in-flight sync; attention states; stop; server-collection
conflicts, unseen-version protection, delete, scopes and read authorization, live hints and unsubscribe,
queries, DI guard, scoped recipe. Browser (Playwright, Chromium/Firefox/WebKit, published sample server
process): static SSR without JavaScript (T46), prerender without JavaScript, Interactive Server live updates
and circuit recreation (T49), WebAssembly writes reaching Server users, Auto first visit on the server and
later visit in WebAssembly (T47), prerender not overwriting unsynced local edits (T48).
