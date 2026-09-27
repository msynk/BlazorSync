# ADR-001: Build a native protocol, adapt an existing system, or combine

- **Status:** Accepted (2026-09-27)
- **Sources:** checked 2026-09-27; see the list at the end. Items marked *unverified* could not be
  confirmed from primary sources in that session.

## Context

BlazorSync's goal is offline-first document replication for .NET applications across Blazor
WebAssembly, Server, Auto, static SSR and Hybrid hosts, with a portable core usable by headless .NET.
Before investing in a native protocol, we compared existing systems on licence, maintenance, .NET/Blazor
WebAssembly support, local store, replication model and backend requirements.

## Comparison

| Option | Licence / service | Maintenance (latest seen) | Blazor WASM in browser | Native .NET / MAUI | Model | Backend |
|---|---|---|---|---|---|---|
| CommunityToolkit Datasync | MIT; no service | Client 10.1.2 (2026-07); active | Online only; the docs say offline mode in WASM "is not recommended, nor is it supported" | Yes (MAUI, WPF, WinUI, Avalonia, Uno), EF Core + SQLite | Operations queue push; delta-token pull on `UpdatedAt`; version/ETag with pluggable resolver | ASP.NET Core + EF Core repositories |
| Dotmim.Sync | MIT; no service | Core 1.3.0 (2025-03); last commit 2025-08, low activity | Not documented (treat as unsupported) | Yes | Relational row change tracking (tracking tables/triggers or SQL Server Change Tracking; mechanism *unverified*, from search snippet) | Relational databases + ASP.NET Core proxy |
| PowerSync .NET SDK | SDK Apache-2.0; service FSL-1.1-ALv2 (source-available); requires PowerSync Service (cloud or self-hosted; self-hosting details *unverified*) | 0.1.5 (2026-09), beta | **No**: "Blazor (web) platforms are not yet supported." | Yes (MAUI, WPF, console) | Server-authoritative buckets/streams with consistent checkpoints; checkpoint does not advance while uploads are pending | PowerSync Service + source DB (list *unverified*) + your upload backend |
| RxDB | Core Apache-2.0; IndexedDB/OPFS/SQLite storages are paid premium | 17.5.0 (2026-08); very active | JS/TS only | No .NET | Document + checkpoint pull/push with assumed-master conflict detection, client-side resolution, `RESYNC` stream | Any backend implementing its endpoints |
| Replicache | Free; licence via terms page (no SPDX); repository archived | 15.3.0 (2025-07); maintenance mode, users directed to Zero | JS only | No | Mutation replay and rebase over authoritative server | Your push/pull endpoints |
| Zero | Apache-2.0 | 1.9.0 (2026-08); active | TS only | No | Optimistic client mutators re-run authoritatively on the server | zero-cache + PostgreSQL ≥ 15 with logical replication |
| ActualLab.Fusion | MIT | 14.4.16 (2026-09); active | Yes (Server + WASM) | Yes (MAUI) | Reactive computed-state cache with invalidation over RPC; persistent client cache; no document merge/conflict model | ASP.NET Core host running Fusion services |
| Electric | Apache-2.0; cloud optional | sync-service 1.8.1 (2026-09); very active | TS clients only | No .NET client | Read-path "shapes" streamed from PostgreSQL; writes via your API | PostgreSQL with logical replication + Electric service |

## Alternatives

1. **Adopt Datasync** and add offline WASM storage. Its offline client is built on EF Core + SQLite, which
   is not available in the browser, and its maintainers state offline WASM is unsupported. We would be
   re-implementing the part we need most.
2. **Adopt PowerSync .NET.** Excludes Blazor web, requires a separate source-available service, and is
   beta.
3. **Adopt Dotmim.Sync.** Relational schema replication, not documents; no browser story; low recent
   activity.
4. **Adopt Fusion** for real-time UI state. Complementary, not a replacement: no merge/conflict model for
   offline writes.
5. **Implement a JS system (RxDB/Zero) behind JS interop.** No native .NET/MAUI story, paid browser
   storage (RxDB) or PostgreSQL + Node service dependency (Zero), and business rules would live in JS.
6. **Build a native protocol** borrowing proven concepts. Chosen.

## Decision

Build a native, portable C# protocol and engine. Borrow concepts, not code:

- From RxDB/CouchDB: document + checkpoint pull, assumed-master conflict detection, client-side
  resolution with three-way context, resync after missed live events.
- From Replicache/Zero and Dexie Cloud: persisted operation identity, server-authoritative outcomes,
  rebase of pending local work over the confirmed state.
- From PowerSync/WatermelonDB: consistent checkpoints, never advancing past pending uploads without
  preserving them, explicit handling of permission and deletion changes.
- From Datasync: server version/ETag concurrency and an ASP.NET Core + EF Core authority shape.

**Differentiator:** one C# application model and conflict policy running identically in Blazor
WebAssembly (IndexedDB), native hosts (SQLite) and server-connected hosts, with an ASP.NET Core authority,
explicit confirmation levels and invariants that are tested, not asserted.

## Consequences and maintenance cost

- We own protocol correctness. Mitigations: numbered invariants, fault-injection and seeded randomized
  tests, and a provider conformance suite (Phase 2/3).
- We own three storage providers (in-memory, SQLite, IndexedDB) and at least one server database. Each
  additional provider must pass conformance, ordering and concurrency tests before it is supported.
- Interop: Fusion may later be offered as an optional notification/observation adapter.

## Migration impact

None; the existing prototype was already a native protocol.

## Sources (accessed 2026-09-27 unless noted)

- https://communitytoolkit.github.io/Datasync/ ; https://communitytoolkit.github.io/Datasync/in-depth/client/ ;
  https://communitytoolkit.github.io/Datasync/in-depth/client/advanced/blazor-wasm/ ; https://github.com/CommunityToolkit/Datasync
- https://github.com/mimetis/dotmim.sync ; https://dotmimsync.readthedocs.io/ ;
  https://dotmimsync.readthedocs.io/ChangeTracking.html (search result only, not fetched)
- https://docs.powersync.com/client-sdks/reference/dotnet ; https://docs.powersync.com/architecture/consistency ;
  https://github.com/powersync-ja/powersync-dotnet
- https://rxdb.info/replication.html ; https://rxdb.info/premium/
- https://doc.replicache.dev/concepts/how-it-works ; https://replicache.dev/
- https://zero.rocicorp.dev/docs/mutators ; https://zero.rocicorp.dev/docs/connecting-to-postgres
- https://github.com/ActualLab/Fusion
- https://github.com/electric-sql/electric
- NuGet and npm registries (versions and dates)
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core: .NET 8 LTS and .NET 9 STS both
  end 2026-11-10; .NET 10 LTS released 2025-11-11, supported until 2028-11-14. This supports the
  `net10.0`-only decision in ADR-012.
