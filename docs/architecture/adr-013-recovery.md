# ADR-013: Recovery of local work and damaged replicas

- **Status:** Accepted (2026-09-28)
- **Invariants:** I01, I04, I14, I17, I19

## Context

A replica can get stuck or damaged in ways the replication protocol does not repair by itself:

- a change the server rejected stays parked until someone acts on it;
- a user wants to drop an unsynchronized change;
- a SQLite file is damaged (bad storage, a copy taken mid-write, a bug outside Bsync);
- local work must move to a new store (a rebuilt file, a new device).

In every case the priority is the same as everywhere else: never lose or duplicate the user's
unsynchronized work silently.

## Decision

- **Parked rejections are explicit.** `ILocalStore.GetRejectedAsync` lists them. `SyncEngine.RetryRejectedAsync`
  resends one as a **new** operation with a fresh timestamp. It never reuses the rejected operation id, because
  the authority keeps the rejection's receipt and would replay it. `ISyncCollection.RetryAsync` exposes the same.
- **Revert** (`SyncEngine.RevertAsync`, `ISyncCollection.RevertAsync`) discards the unsynchronized change of one
  document and returns it to the newest server state the replica knows: the observed state if one is newer,
  otherwise the base. A document the server never confirmed is hidden like a record missing after reset, not
  deleted. The next pull shows it if the server has it. An operation already sent cannot be recalled; the
  pull shows the server's decision. Kept conflicts are separate (ADR-006).
- **Export/import.** `ExportLocalChangesAsync` returns every record holding local work (pending, rejected,
  kept conflicts) with its pending operation. `ImportLocalChangesAsync` adds such records to another store
  only where that store has no local work for the id. Operation ids are preserved, so an operation that
  already reached the server is replayed from its receipt, never applied twice. The engine clock moves past
  every imported timestamp.
- **SQLite rebuild** (`SqliteStoreRecovery`):
  - `CheckAsync` runs `quick_check`.
  - `RebuildAsync` moves the database, WAL and shared-memory files aside. It **never deletes** them. It then
    creates a fresh database in their place.
  - It copies only rows holding local work (`is_dirty` or a kept conflict). It reads the table in rowid order
    and in small ranges. After a read error it probes further ahead with growing steps, so a damaged page
    costs only its own rows.
  - Rows that are readable but inconsistent are skipped and counted. These are rows with malformed JSON, an
    invalid timestamp, or a key that does not match the id.
  - Each collection restarts from the start of the feed in a generation newer than any salvaged record, in
    resnapshot mode. Server state wins for clean data, and records the server no longer has are hidden after
    the first complete pull (protocol §6.1).
  - The new file gets a new replica id and incarnation. The clock high-water mark is carried over.
- **Domain schema upgrades** use the existing `upgrade-required` path. During a window the server accepts
  both schema ids. After the old id is dropped, old clients stop with a permanent `upgrade-required` error
  and keep their work. The upgraded app uploads that work from the same replica, with the same operation ids.

## Alternatives

- **Deleting a damaged file and resyncing.** Rejected: it silently loses unsynchronized work.
- **Copying the whole damaged database with `VACUUM INTO` or the SQLite `.recover` extension.** Not used:
  - `VACUUM INTO` stops at the first damaged page.
  - `.recover` is a CLI/extension feature that is not available through `Microsoft.Data.Sqlite`.
  - Clean data is cheaper and safer to pull again from the server than to trust from a damaged file.
- **An upcasting hook in the stores and transports.** Not done. Every reader already deserializes with the
  document type's contract, so the upgrade lives in the type: `[JsonExtensionData]` plus `IJsonOnDeserialized`,
  with `DocumentUpgrade.TryTake` to move old members (tested through SQLite and the wire in
  `DocumentUpgradeTests`).
  - Limitation: an upload that is in flight across a shape change is fingerprinted by the server with the old
    shape. Its resend with the upgraded payload is answered `operation-id-reused`. Let queues drain before
    changing a document's shape, or keep both shapes readable during the roll-out window.

## Consequences

- An application decides when to run the check. On startup it is cheap for small replicas. After a
  `SqliteException` with a corruption code it is mandatory. The damaged copy contains user data; treat it
  like the database itself.
- Salvage is best effort: records on unreadable pages cannot be counted, and the report says reading was
  incomplete.
- IndexedDB has no equivalent. Browsers do not expose partially damaged databases. Eviction is detected by a
  new replica id and the replica is repopulated from the server (ADR-008); unsynchronized work in an evicted
  database is lost. Apps should call `IndexedDbLocalStore.RequestPersistenceAsync` (not done automatically; some
  browsers prompt the user).

## Tests

- `RecoveryTests`:
  - retry and revert, including revert after a lost response;
  - export/import with in-flight, queued, rejected and conflicted work;
  - rebuild of a healthy file, of a file that is not a database, of a file with damaged pages (369 of 400
    rows salvaged in the recorded run), and of a file with a damaged row.
- `SchemaUpgradeTests.RollingUpgrade`.
- `BlazorIntegrationTests.RetryAndRevertThroughCollection`.
- Store conformance case for rejected records.
