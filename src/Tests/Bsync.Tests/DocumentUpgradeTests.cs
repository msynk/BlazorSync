using System.Text.Json;
using Bsync.Clocks;
using Bsync.Documents;
using Bsync.Server;
using Bsync.Storage;
using Bsync.Storage.Sqlite;
using Bsync.Tests.Sqlite;
using Xunit;

namespace Bsync.Tests;

/// <summary>I17: documents written by an older application version are upgraded wherever they are read.</summary>
public sealed class DocumentUpgradeTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static InMemorySyncServer<T> Server<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class, ISyncEntity => new(new InMemorySyncServerOptions<T>
        {
            Cloner = DocumentCloner.Json(type),
            Fingerprint = DocumentCloner.JsonFingerprint(type),
            PhysicalClock = SystemPhysicalClock.Instance,
        });

    [Fact(DisplayName = "I01 I17: a version 1 replica with pending work is opened by version 2, upgraded, and uploaded in the new shape")]
    public async Task LocalReplicaIsUpgraded()
    {
        var options = new SqliteLocalStoreOptions { DataSource = _database.Path, Collection = "tasks" };
        var v1Store = await SqliteLocalStore<TaskV1>.OpenAsync(options, TaskJson.Default.TaskV1);
        var v1 = new SyncEngine<TaskV1>(v1Store, new InProcessTransport<TaskV1>(Server(TaskJson.Default.TaskV1)), new HybridLogicalClock("device"), DocumentCloner.Json(TaskJson.Default.TaskV1));
        await v1.WriteAsync(new TaskV1 { Id = "t1", Title = "written by version 1" });
        SqliteStorePool.Release(_database.Path);

        // The app is upgraded; its server speaks version 2.
        var server = Server(TaskJson.Default.TaskV2);
        var v2Store = await SqliteLocalStore<TaskV2>.OpenAsync(options, TaskJson.Default.TaskV2);
        var v2 = new SyncEngine<TaskV2>(v2Store, new InProcessTransport<TaskV2>(server), new HybridLogicalClock("device"), DocumentCloner.Json(TaskJson.Default.TaskV2));

        var local = (await v2.GetAsync("t1"))!.Current;
        Assert.Equal("written by version 1", local.Heading);
        Assert.True(local.Unknown is null or { Count: 0 }); // the old member was moved, not kept

        Assert.True((await v2.SyncAsync()).IsComplete);
        var stored = JsonSerializer.Serialize(server.Snapshot().Single(), TaskJson.Default.TaskV2);
        Assert.Contains("\"Heading\":\"written by version 1\"", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Title", stored, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "I17: a version 1 document on the server is upgraded when a version 2 replica pulls it; a version 1 replica keeps version 2 members")]
    public async Task WireDocumentsAreUpgraded()
    {
        var v1Server = Server(TaskJson.Default.TaskV1);
        var v1 = new SyncEngine<TaskV1>(new InMemoryLocalStore<TaskV1>(DocumentCloner.Json(TaskJson.Default.TaskV1)), new InProcessTransport<TaskV1>(v1Server), new HybridLogicalClock("old"), DocumentCloner.Json(TaskJson.Default.TaskV1));
        await v1.WriteAsync(new TaskV1 { Id = "t1", Title = "old shape" });
        await v1.SyncAsync();

        // A version 2 client reads the same data through the JSON encoding (as it would over HTTP).
        var json = JsonSerializer.Serialize(v1Server.Snapshot().Single(), TaskJson.Default.TaskV1);
        var upgraded = JsonSerializer.Deserialize(json, TaskJson.Default.TaskV2)!;
        Assert.Equal("old shape", upgraded.Heading);

        // A version 1 client editing a version 2 document keeps the members it does not know.
        var fromV2 = JsonSerializer.Deserialize(JsonSerializer.Serialize(new TaskV2 { Id = "t2", Heading = "new shape", Done = true }, TaskJson.Default.TaskV2), TaskJson.Default.TaskV1)!;
        fromV2.Title = "edited by version 1";
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(fromV2, TaskJson.Default.TaskV1), TaskJson.Default.TaskV2)!;
        Assert.Equal(("new shape", true), (roundTrip.Heading, roundTrip.Done));
    }
}
