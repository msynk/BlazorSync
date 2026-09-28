using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Bsync;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;

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
