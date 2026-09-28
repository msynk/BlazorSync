using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Bsync;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;

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
