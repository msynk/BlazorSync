using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;
using BlazorSync;
using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Documents;
using BlazorSync.Server;
using BlazorSync.Storage;
using BlazorSync.Storage.Sqlite;

BenchmarkSwitcher.FromAssembly(typeof(Workload).Assembly).Run(args);

/// <summary>A document of about 1 KiB (ADR-012 functional workload).</summary>
public sealed class BenchDocument : ISyncEntity
{
    public string Id { get; set; } = string.Empty;

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int Priority { get; set; }
}

[JsonSerializable(typeof(BenchDocument))]
public sealed partial class BenchJson : JsonSerializerContext;

/// <summary>Shared setup: stores, servers and documents.</summary>
public static class Workload
{
    public static readonly string Body = new('x', 960);

    public static readonly Func<BenchDocument, BenchDocument> Clone = DocumentCloner.Json(BenchJson.Default.BenchDocument);

    public static BenchDocument Document(int i) => new() { Id = $"doc-{i:D6}", Title = $"Document {i}", Body = Body, Priority = i % 5 };

    public static InMemorySyncServer<BenchDocument> Server() => new(new InMemorySyncServerOptions<BenchDocument>
    {
        Cloner = Clone,
        Fingerprint = DocumentCloner.JsonFingerprint(BenchJson.Default.BenchDocument),
        MaxOperationsPerPush = 1000,
        MaxPageSize = 1000,
    });

    public static async Task<ILocalStore<BenchDocument>> StoreAsync(string kind, string directory) => kind switch
    {
        "memory" => new InMemoryLocalStore<BenchDocument>(Clone),
        "sqlite-full" => await SqliteLocalStore<BenchDocument>.OpenAsync(new SqliteLocalStoreOptions { DataSource = Path.Combine(directory, $"{Guid.NewGuid():N}.db"), Durability = SqliteDurability.Full }, BenchJson.Default.BenchDocument),
        "sqlite-normal" => await SqliteLocalStore<BenchDocument>.OpenAsync(new SqliteLocalStoreOptions { DataSource = Path.Combine(directory, $"{Guid.NewGuid():N}.db"), Durability = SqliteDurability.Normal }, BenchJson.Default.BenchDocument),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Queues <paramref name="count"/> local writes in one store transaction (the setup is not what is measured).</summary>
    public static Task SeedPendingAsync(ILocalStore<BenchDocument> store, int count, string node)
    {
        var clock = new HybridLogicalClock(node);
        return store.UpdateAsync(Enumerable.Range(0, count).Select(i =>
        {
            var document = Document(i);
            document.UpdatedAt = clock.Now();
            return new RecordUpdate<BenchDocument>(document.Id, _ => new SyncRecord<BenchDocument>(document, null, IsDirty: true) { LocalRevision = 1 });
        }).ToList());
    }

    public static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "blazorsync-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void Delete(string directory)
    {
        SqliteConnectionPools.ReleaseAll(directory);
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class SqliteConnectionPools
{
    public static void ReleaseAll(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.db"))
        {
            SqliteStorePool.Release(file);
        }
    }
}

/// <summary>Local UX: one durable local write (target p95 &lt; 50 ms on a reference device).</summary>
[MemoryDiagnoser]
public class LocalWrite
{
    private string _directory = string.Empty;
    private SyncEngine<BenchDocument> _engine = null!;
    private int _next;

    [Params("memory", "sqlite-full", "sqlite-normal")]
    public string Store { get; set; } = "memory";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _directory = Workload.TempDirectory();
        var store = await Workload.StoreAsync(Store, _directory);
        _engine = new SyncEngine<BenchDocument>(store, new InProcessTransport<BenchDocument>(Workload.Server()), new HybridLogicalClock("bench"), Workload.Clone);
    }

    [GlobalCleanup]
    public void Cleanup() => Workload.Delete(_directory);

    [Benchmark]
    public Task<LocalWriteReceipt> Write() => _engine.WriteAsync(Workload.Document(Interlocked.Increment(ref _next) % 10_000));
}

/// <summary>Local UX: a small indexed read and a full query over 10,000 documents of ~1 KiB.</summary>
[MemoryDiagnoser]
public class LocalRead
{
    private string _directory = string.Empty;
    private ILocalStore<BenchDocument> _store = null!;
    private int _next;

    [Params("memory", "sqlite-full")]
    public string Store { get; set; } = "memory";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _directory = Workload.TempDirectory();
        _store = await Workload.StoreAsync(Store, _directory);
        await Workload.SeedPendingAsync(_store, 10_000, "seed");
    }

    [GlobalCleanup]
    public void Cleanup() => Workload.Delete(_directory);

    [Benchmark]
    public Task<SyncRecord<BenchDocument>?> GetById() => _store.GetAsync($"doc-{Interlocked.Increment(ref _next) * 7919 % 10_000:D6}");

    [Benchmark]
    public Task<IReadOnlyList<BenchDocument>> QueryAll() => _store.QueryAsync();
}

/// <summary>Reconnect: 10,000 queued writes converge in one sync (in-process authority; no network).</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class Reconnect
{
    private string _directory = string.Empty;
    private SyncEngine<BenchDocument> _engine = null!;

    [Params("memory", "sqlite-full")]
    public string Store { get; set; } = "memory";

    [IterationSetup]
    public void Setup()
    {
        _directory = Workload.TempDirectory();
        var store = Workload.StoreAsync(Store, _directory).GetAwaiter().GetResult();
        Workload.SeedPendingAsync(store, 10_000, "device").GetAwaiter().GetResult();
        _engine = new SyncEngine<BenchDocument>(store, new InProcessTransport<BenchDocument>(Workload.Server()), new HybridLogicalClock("device"), Workload.Clone,
            options: new SyncOptions<BenchDocument> { PushBatchSize = 500, MaxPushBatches = 100 });
    }

    [IterationCleanup]
    public void Cleanup() => Workload.Delete(_directory);

    [Benchmark]
    public async Task<SyncResult> SyncTenThousandQueuedWrites()
    {
        var result = await _engine.SyncAsync();
        return result.IsComplete && result.Pushed == 10_000 ? result : throw new InvalidOperationException($"Incomplete: {result}");
    }
}

/// <summary>Functional: a new replica pulls 10,000 documents of ~1 KiB.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class InitialPull
{
    private InMemorySyncServer<BenchDocument> _server = null!;
    private string _directory = string.Empty;
    private SyncEngine<BenchDocument> _engine = null!;

    [Params("memory", "sqlite-full")]
    public string Store { get; set; } = "memory";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _server = Workload.Server();
        var writer = new SyncEngine<BenchDocument>(new InMemoryLocalStore<BenchDocument>(Workload.Clone), new InProcessTransport<BenchDocument>(_server), new HybridLogicalClock("writer"), Workload.Clone,
            options: new SyncOptions<BenchDocument> { PushBatchSize = 1000 });
        for (var i = 0; i < 10_000; i++)
        {
            await writer.WriteAsync(Workload.Document(i));
        }

        await writer.SyncAsync();
    }

    [IterationSetup]
    public void Setup()
    {
        _directory = Workload.TempDirectory();
        var store = Workload.StoreAsync(Store, _directory).GetAwaiter().GetResult();
        _engine = new SyncEngine<BenchDocument>(store, new InProcessTransport<BenchDocument>(_server), new HybridLogicalClock("reader"), Workload.Clone,
            options: new SyncOptions<BenchDocument> { PullBatchSize = 500 });
    }

    [IterationCleanup]
    public void Cleanup() => Workload.Delete(_directory);

    [Benchmark]
    public async Task<SyncResult> PullTenThousandDocuments()
    {
        var result = await _engine.PullAsync();
        return result.Pulled == 10_000 ? result : throw new InvalidOperationException($"Pulled {result.Pulled}");
    }
}

/// <summary>Conflict handling: a field-level three-way merge of a ~1 KiB document.</summary>
[MemoryDiagnoser]
public class Merge
{
    private readonly BenchDocument _base = Workload.Document(1);
    private readonly BenchDocument _local = Workload.Document(1);
    private readonly BenchDocument _server = Workload.Document(1);

    [GlobalSetup]
    public void Setup()
    {
        _local.Title = "edited locally";
        _server.Priority = 42;
    }

    [Benchmark]
    public ThreeWayMergeResult<BenchDocument> ThreeWay() => ThreeWayMerge.Merge(_base, _local, _server, BenchJson.Default.BenchDocument);
}
