using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Bsync;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage.Sqlite;

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
