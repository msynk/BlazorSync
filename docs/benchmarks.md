# Benchmarks

Measurements of the [ADR-012](architecture/adr-012-packaging-and-support.md) workloads with BenchmarkDotNet
(`src/Bsync.Benchmarks`). They were recorded on one development machine in short runs. Treat them as orders
of magnitude and as a baseline for regressions, not as performance claims for any device.

```bash
dotnet run -c Release --project src/Bsync.Benchmarks -- --filter "*LocalWrite*" "*LocalRead*" "*Merge*" --job short
dotnet run -c Release --project src/Bsync.Benchmarks -- --filter "*Reconnect*"
dotnet run -c Release --project src/Bsync.Benchmarks -- --filter "*InitialPull*"
dotnet run -c Release --project src/Bsync.Benchmarks -- --filter "*ScalePull*"
```

## Recorded run (2026-09-28)

The run used BenchmarkDotNet 0.15.8 and .NET 10.0.12 (x64 RyuJIT) on Windows 10 22H2, with an Intel Core Ultra 7
255H. The storage device was not recorded. The documents were about 1 KiB. The authority was the in-memory one, called in-process, so
there was no network.

### Local UX: durable write and indexed read (target: p95 under 50 ms on a reference device)

These are ShortRun results (3 iterations), so the table gives means, not p95.

| Operation | Store | Mean | Allocated |
|---|---|---:|---:|
| `WriteAsync` (commit + queue) | in-memory | 9.3 µs | 14.8 KB |
| `WriteAsync` | SQLite, `synchronous=FULL` | 850 µs | 26.7 KB |
| `WriteAsync` | SQLite, `synchronous=NORMAL` | 258 µs | 31.2 KB |
| `GetAsync` by id, 10,000 documents | in-memory | 0.88 µs | 1.9 KB |
| `GetAsync` by id, 10,000 documents | SQLite | 29.6 µs | 7.5 KB |
| `QueryAsync`, all 10,000 documents | in-memory | 38.8 ms | 33.5 MB |
| `QueryAsync`, all 10,000 documents | SQLite | 57.2 ms | 43.4 MB |

### Reconnect: 10,000 queued writes converge in one `SyncAsync`

The push batch was 500 operations. Each run was a Monitoring run of 5 iterations with a fresh store.

| Store | Mean | Allocated |
|---|---:|---:|
| in-memory | 824 ms | 643 MB |
| SQLite, `synchronous=FULL` | 2.36 s | 900 MB |

Every run asserts that all 10,000 operations were accepted and nothing was left over.

### Functional: a new replica pulls 10,000 documents

The page size was 500. The run used the same job as the reconnect benchmark.

| Store | Mean | Allocated |
|---|---:|---:|
| in-memory | 243 ms | 239 MB |
| SQLite, `synchronous=FULL` | 1.00 s | 348 MB |

### Scale: a new replica pulls 100,000 documents

The page size was 1,000. Each run was a Monitoring run of 2 iterations. It was run twice: once alongside other
work on the machine, and once alone.

| Store | Mean (run alone) | Mean (other run) | Allocated |
|---|---:|---:|---:|
| in-memory | 6.8 s (±2.6 s between iterations) | 5.3 s | 2.43 GB |
| SQLite, `synchronous=FULL` | 14.3 s | 16.4 s | 3.57 GB |

Allocated is the total over the run, not peak memory. Time grows roughly linearly from 10,000 documents (about
10× for 10× the data).

### Conflict handling

| Operation | Mean | Allocated |
|---|---:|---:|
| `ThreeWayMerge.Merge` of a ~1 KiB document, disjoint edits | 12.6 µs | 22.8 KB |

## Observations

- A durable SQLite write stays around a millisecond on this machine, far inside the 50 ms target. A
  reference *mobile* device has not been measured.
- `QueryAsync` materializes the whole collection. It takes tens of milliseconds at 10,000 documents, so large
  collections need paging or indexed queries (not implemented).
- Allocation per synced document is high: about 64 KB per document to push and 24 KB to pull. JSON cloning
  for isolation and fingerprinting dominates. This is the first thing to optimize if memory matters.
- Not measured:
  - peak memory;
  - PostgreSQL throughput with concurrent sessions (the authority exists; no benchmark yet);
  - IndexedDB in browsers;
  - network latency.
