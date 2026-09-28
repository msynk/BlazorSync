using System.Globalization;
using System.Text.Json.Serialization;
using Bsync;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Transport;

// Usage:
//   writes <db> <count>          local writes through SyncEngine; prints "committed <i> <timestamp>" after each receipt
//   pages  <db> <count> <size>   atomic page commits (records + cursor); prints "committed <i>" after each commit
// The test process kills this process at an arbitrary point and inspects the database afterwards.

var mode = args[0];
var store = await SqliteLocalStore<CrashNote>.OpenAsync(
    new SqliteLocalStoreOptions { DataSource = args[1], Collection = "notes" },
    CrashJson.Default.CrashNote);
var count = int.Parse(args[2], CultureInfo.InvariantCulture);

switch (mode)
{
    case "writes":
        var engine = new SyncEngine<CrashNote>(store, new NoNetwork(), new HybridLogicalClock("crash-host"), DocumentCloner.Json(CrashJson.Default.CrashNote));
        for (var i = 0; i < count; i++)
        {
            var receipt = await engine.WriteAsync(new CrashNote { Id = $"w{i:D6}", Title = new string('x', 200) });
            Console.WriteLine($"committed {i} {receipt.UpdatedAt}");
        }

        break;

    case "pages":
        var size = int.Parse(args[3], CultureInfo.InvariantCulture);
        for (var i = 0; i < count; i++)
        {
            var batch = i;
            var updates = Enumerable.Range(0, size)
                .Select(j => new RecordUpdate<CrashNote>(
                    $"b{batch:D6}-{j:D3}",
                    _ => new SyncRecord<CrashNote>(new CrashNote { Id = $"b{batch:D6}-{j:D3}", Title = new string('y', 200) }, null, IsDirty: false) { BaseVersion = batch + 1 }))
                .ToList();
            await store.UpdateAsync(updates, new ReplicaCursor(new Checkpoint($"cp{batch}"), 0, false));
            Console.WriteLine($"committed {i}");
        }

        break;

    default:
        throw new ArgumentException($"Unknown mode '{mode}'.");
}

// Same JSON shape as the tests' Note type (property names are not changed by a naming policy).
public sealed class CrashNote : ISyncEntity
{
    public string Id { get; set; } = string.Empty;

    public HlcTimestamp UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
}

[JsonSerializable(typeof(CrashNote))]
public sealed partial class CrashJson : JsonSerializerContext;

internal sealed class NoNetwork : ISyncTransport<CrashNote>
{
    public Task<PullResult<CrashNote>> PullAsync(PullRequest request, CancellationToken cancellationToken = default) => throw new IOException("offline");

    public Task<PushResult<CrashNote>> PushAsync(PushRequest<CrashNote> request, CancellationToken cancellationToken = default) => throw new IOException("offline");

    public IAsyncEnumerable<StreamEvent<CrashNote>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) => throw new IOException("offline");
}
