using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Bsync;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage.Sqlite;

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
