using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Bsync;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;

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
