using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests;

/// <summary>I03 I15 I16: observers see each committed transaction once, after commit; disposal stops delivery.</summary>
public sealed class ObservationTests
{
    [Fact(DisplayName = "I16: local writes, pull pages and push metadata are each reported once per commit")]
    public async Task ReportsEachCommit()
    {
        var server = InMemorySyncServerRef.Create();
        var writer = new TestReplica(server, "w");
        for (var i = 0; i < 5; i++)
        {
            await writer.Engine.WriteAsync(new Note { Id = $"r{i}" });
        }

        await writer.Engine.SyncAsync();

        var client = new TestReplica(server, "a", options: new SyncOptions<Note> { PullBatchSize = 3 });
        var seen = new List<SyncChange>();
        using var _ = client.Engine.Observe(seen.Add);

        await client.Engine.WriteAsync(new Note { Id = "local" });
        await client.Engine.SyncAsync();

        Assert.Equal(SyncChangeKind.Local, seen[0].Kind);
        Assert.Equal(["local"], seen[0].Ids);
        Assert.Equal([3, 2], seen.Where(c => c.Kind == SyncChangeKind.Remote).Select(c => c.Ids.Count));
        Assert.Equal(["r0", "r1", "r2", "r3", "r4"], seen.Where(c => c.Kind == SyncChangeKind.Remote).SelectMany(c => c.Ids));
        Assert.Equal(2, seen.Count(c => c.Kind == SyncChangeKind.Sync)); // operation prepared, then acknowledged
        Assert.All(seen, c => Assert.NotEmpty(c.Ids));
    }

    [Fact(DisplayName = "I03: observers run after the commit is visible")]
    public async Task ObserverSeesCommittedState()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        string? observedTitle = null;
        using var _ = client.Engine.Observe(change => observedTitle = client.Engine.GetAsync(change.Ids[0]).Result?.Current.Title);

        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "committed" });

        Assert.Equal("committed", observedTitle);
    }

    [Fact(DisplayName = "I16: a sync that changes nothing reports nothing")]
    public async Task NoChangeNoNotification()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        var count = 0;
        using var _ = client.Engine.Observe(_ => count++);

        await client.Engine.SyncAsync();
        await client.Engine.DeleteAsync("unknown");

        Assert.Equal(0, count);
    }

    [Fact(DisplayName = "I15: a throwing observer does not affect replication and its error is reported")]
    public async Task ThrowingObserverIsIsolated()
    {
        var server = InMemorySyncServerRef.Create();
        var client = new TestReplica(server, "a");
        var errors = new List<Exception>();
        var healthy = 0;
        using var bad = client.Engine.Observe(_ => throw new InvalidOperationException("ui bug"), errors.Add);
        using var good = client.Engine.Observe(_ => healthy++);

        await client.Engine.WriteAsync(new Note { Id = "n1" });
        var result = await client.Engine.SyncAsync();

        Assert.True(result.IsComplete);
        Assert.Equal("n1", server.Get("n1").Id);
        Assert.Equal(healthy, errors.Count);
        Assert.True(healthy >= 3);
    }

    [Fact(DisplayName = "I15: disposing a subscription stops delivery, and disposing twice is harmless")]
    public async Task DisposeStops()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        var count = 0;
        var subscription = client.Engine.Observe(_ => count++);

        await client.Engine.WriteAsync(new Note { Id = "n1" });
        subscription.Dispose();
        subscription.Dispose();
        await client.Engine.WriteAsync(new Note { Id = "n2" });

        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "I10 I14: records marked missing after a reset are reported as remote changes")]
    public async Task ResetMissingReported()
    {
        var server = InMemorySyncServerRef.Create();
        var client = new TestReplica(server, "a");
        var backup = server.Server.CreateBackup();
        await client.Engine.WriteAsync(new Note { Id = "lost" });
        await client.Engine.SyncAsync();
        server.Restore(backup);

        var seen = new List<SyncChange>();
        using var _ = client.Engine.Observe(seen.Add);
        await client.Engine.SyncAsync();

        Assert.Contains(seen, c => c.Kind == SyncChangeKind.Remote && c.Ids.SequenceEqual(["lost"]));
    }
}
