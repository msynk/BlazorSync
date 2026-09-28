using Bsync.Storage;
using Bsync.Tests.TestSupport;
using Xunit;

namespace Bsync.Tests;

/// <summary>Single-flight replication, responsiveness during network waits, cancellation and simulated crashes.</summary>
public sealed class ConcurrencyAndRecoveryTests
{
    [Fact(DisplayName = "T10 I15: overlapping sync calls never run two replication workers at once")]
    public async Task OverlappingSyncsAreSingleFlight()
    {
        var server = InMemorySyncServerRef.Create();
        var client = new TestReplica(server, "a", options: new SyncOptions<Note> { PushBatchSize = 1 });
        for (var i = 0; i < 5; i++)
        {
            await client.Engine.WriteAsync(new Note { Id = $"n{i}" });
        }

        client.Transport.BeforePush = _ => Task.Delay(5);
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => client.Engine.SyncAsync())));

        Assert.Equal(1, client.Transport.MaxConcurrentPushes);
        Assert.Equal(5, runs.Sum(r => r.Pushed));
        Assert.Equal(5, client.Transport.PushLog.SelectMany(r => r.Operations).Select(o => o.OperationId).Distinct().Count());
        Assert.Equal(5, server.Server.Snapshot().Count);
    }

    [Fact(DisplayName = "T06 I02: local writes complete while a push waits on the network")]
    public async Task LocalWritesStayResponsiveDuringPush()
    {
        var server = InMemorySyncServerRef.Create();
        var client = new TestReplica(server, "a");
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "v1" });

        var release = new TaskCompletionSource();
        var inFlight = new TaskCompletionSource();
        client.Transport.BeforePush = async _ =>
        {
            client.Transport.BeforePush = null;
            inFlight.SetResult();
            await release.Task;
        };

        var push = client.Engine.PushAsync();
        await inFlight.Task;
        var receipt = await client.Engine.WriteAsync(new Note { Id = "n1", Title = "v2" }).WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        await push;

        Assert.Equal(2, receipt.LocalRevision);
        Assert.Equal("v2", server.Get("n1").Title);
        Assert.False((await client.RecordAsync("n1")).IsDirty);
    }

    [Fact(DisplayName = "T16 I15: cancellation before the local commit writes nothing")]
    public async Task CancelledWriteCommitsNothing()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Engine.WriteAsync(new Note { Id = "n1" }, cts.Token));
        Assert.Null(await client.Engine.GetAsync("n1"));
    }

    [Fact(DisplayName = "T11 I15: cancellation after the server committed is treated as an unknown outcome")]
    public async Task CancellationAfterServerCommit()
    {
        var server = InMemorySyncServerRef.Create();
        var client = new TestReplica(server, "a");
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "once" });
        using var cts = new CancellationTokenSource();
        client.Transport.AfterPush = async r =>
        {
            client.Transport.AfterPush = null;
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            return r;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Engine.PushAsync(cts.Token));
        Assert.NotNull((await client.RecordAsync("n1")).Pending);

        var retry = await client.Engine.PushAsync();
        Assert.Equal(1, retry.Pushed);
        Assert.Equal(1, server.Server.ReceiptCount);
        Assert.False((await client.RecordAsync("n1")).IsDirty);
    }

    [Fact(DisplayName = "T17 I01 I04: crash after the operation is persisted but before send resends the same id")]
    public async Task CrashBeforeSend()
    {
        var server = InMemorySyncServerRef.Create();
        var durable = new InMemoryLocalStore<Note>(NoteJson.Clone);
        var first = new TestReplica(server, "a", store: durable);
        await first.Engine.WriteAsync(new Note { Id = "n1" });
        first.Transport.FailBeforeSend = true;
        await Assert.ThrowsAsync<InjectedFaultException>(() => first.Engine.PushAsync());
        var persistedId = (await first.RecordAsync("n1")).Pending!.OperationId;

        var restarted = new TestReplica(server, "a", store: durable);
        await restarted.Engine.SyncAsync();

        Assert.Equal(persistedId, restarted.Transport.PushLog.Single().Operations.Single().OperationId);
        Assert.False((await restarted.RecordAsync("n1")).IsDirty);
    }

    [Fact(DisplayName = "T18 I04: crash after the server committed but before the local acknowledgement does not duplicate")]
    public async Task CrashBeforeLocalAcknowledgement()
    {
        var server = InMemorySyncServerRef.Create();
        var durable = new InMemoryLocalStore<Note>(NoteJson.Clone);
        var first = new TestReplica(server, "a", store: durable);
        await first.Engine.WriteAsync(new Note { Id = "n1", Title = "x" });
        first.Store.BeforeUpdate = (call, _, _) => call == 3 ? throw new InjectedFaultException("crash before ack") : Task.CompletedTask;
        await Assert.ThrowsAsync<InjectedFaultException>(() => first.Engine.PushAsync());
        var committedVersion = server.Server.GetVersion("n1");

        var restarted = new TestReplica(server, "a", store: durable);
        var result = await restarted.Engine.SyncAsync();

        Assert.Equal(0, result.Conflicts);
        Assert.Equal(committedVersion, server.Server.GetVersion("n1"));
        Assert.Equal(committedVersion, (await restarted.RecordAsync("n1")).BaseVersion);
        Assert.False((await restarted.RecordAsync("n1")).IsDirty);
    }

    [Fact(DisplayName = "I02: records returned by the store are independent copies")]
    public async Task StoreSnapshotsDoNotAlias()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "x" });

        var record = await client.RecordAsync("n1");
        record.Current.Title = "mutated";
        (await client.Engine.QueryAsync())[0].Title = "mutated too";

        Assert.Equal("x", (await client.RecordAsync("n1")).Current.Title);
    }
}
