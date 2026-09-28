using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Bsync;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage.Sqlite;

/// <summary>Scale: a new replica pulls 100,000 documents of ~1 KiB in bounded pages (ADR-012 scale workload).</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 0, iterationCount: 2)]
public class ScalePull
{
    private InMemorySyncServer<BenchDocument> _server = null!;
    private string _directory = string.Empty;
    private SyncEngine<BenchDocument> _engine = null!;

    [Params("memory", "sqlite-full")]
    public string Store { get; set; } = "memory";

    [GlobalSetup]
    public void SetupServer()
    {
        _server = Workload.Server();
        var clock = new HybridLogicalClock("writer");
        for (var start = 0; start < 100_000; start += 1000)
        {
            var operations = Enumerable.Range(start, 1000).Select(i =>
            {
                var document = Workload.Document(i);
                document.UpdatedAt = clock.Now();
                return new Bsync.Protocol.PushOperation<BenchDocument>(Guid.NewGuid().ToString("N"), document.Id, null, document);
            }).ToList();
            _server.Push(new Bsync.Protocol.PushRequest<BenchDocument>(operations));
        }
    }

    [IterationSetup]
    public void Setup()
    {
        _directory = Workload.TempDirectory();
        var store = Workload.StoreAsync(Store, _directory).GetAwaiter().GetResult();
        _engine = new SyncEngine<BenchDocument>(store, new InProcessTransport<BenchDocument>(_server), new HybridLogicalClock("reader"), Workload.Clone,
            options: new SyncOptions<BenchDocument> { PullBatchSize = 1000, MaxPullPages = 1000 });
    }

    [IterationCleanup]
    public void Cleanup() => Workload.Delete(_directory);

    [Benchmark]
    public async Task<SyncResult> PullHundredThousandDocuments()
    {
        var result = await _engine.PullAsync();
        return result.Pulled == 100_000 ? result : throw new InvalidOperationException($"Pulled {result.Pulled}");
    }
}
