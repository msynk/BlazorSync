using BlazorSync.Protocol;
using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests;

/// <summary>Pull paging, atomic page application, monotonic versions and malformed pages.</summary>
public sealed class PullProtocolTests
{
    private static async Task<InMemorySyncServerRef> SeededServerAsync(int count)
    {
        var server = InMemorySyncServerRef.Create();
        var writer = new TestReplica(server, "w");
        for (var i = 0; i < count; i++)
        {
            await writer.Engine.WriteAsync(new Note { Id = $"n{i:D2}", Title = $"t{i}" });
        }

        await writer.Engine.SyncAsync();
        return server;
    }

    [Fact(DisplayName = "I03 I08: pull pages until the feed is drained")]
    public async Task PullsAllPages()
    {
        var server = await SeededServerAsync(7);
        var client = new TestReplica(server, "a", options: new SyncOptions<Note> { PullBatchSize = 3 });

        var result = await client.Engine.PullAsync();

        Assert.Equal(7, result.Pulled);
        Assert.True(result.IsComplete);
        Assert.Equal(3, client.Transport.PullCalls);
    }

    [Fact(DisplayName = "I08: the page budget stops at a committed checkpoint and reports remaining work")]
    public async Task PageBudget()
    {
        var server = await SeededServerAsync(7);
        var client = new TestReplica(server, "a", options: new SyncOptions<Note> { PullBatchSize = 2, MaxPullPages = 2 });

        var first = await client.Engine.PullAsync();
        var second = await client.Engine.PullAsync();

        Assert.Equal(4, first.Pulled);
        Assert.True(first.HasRemainingWork);
        Assert.Equal(3, second.Pulled);
        Assert.True(second.IsComplete);
    }

    [Fact(DisplayName = "T19 I03: a crash halfway through applying a page commits nothing, including the checkpoint")]
    public async Task CrashDuringPageApplyIsAtomic()
    {
        var server = await SeededServerAsync(4);
        var client = new TestReplica(server, "a", options: new SyncOptions<Note> { PullBatchSize = 2 });
        var before = await client.Store.GetCheckpointAsync();

        // The second page's transform throws on its second record.
        var pages = 0;
        client.Store.BeforeUpdate = (_, updates, checkpoint) =>
        {
            if (checkpoint is not null && ++pages == 2)
            {
                client.Store.BeforeUpdate = null;
                return client.Store.Inner.UpdateAsync(
                    [updates[0], new(updates[1].Id, _ => throw new InjectedFaultException("crash mid-page"))],
                    checkpoint);
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InjectedFaultException>(() => client.Engine.PullAsync());

        Assert.Equal(2, (await client.Engine.QueryAsync()).Count);
        Assert.NotEqual(before, await client.Store.GetCheckpointAsync());

        // Resuming continues from the first page's checkpoint and converges.
        var resumed = await client.Engine.PullAsync();
        Assert.Equal(2, resumed.Pulled);
        Assert.Equal(4, (await client.Engine.QueryAsync()).Count);
    }

    [Fact(DisplayName = "T20 I03: remote state and checkpoint are committed together")]
    public async Task CheckpointCommittedWithPage()
    {
        var server = await SeededServerAsync(2);
        var client = new TestReplica(server, "a");
        Checkpoint? committed = null;
        client.Store.BeforeUpdate = (_, updates, checkpoint) =>
        {
            if (checkpoint is not null)
            {
                committed = checkpoint;
                Assert.Equal(2, updates.Count);
            }

            return Task.CompletedTask;
        };

        await client.Engine.PullAsync();

        Assert.Equal(committed, await client.Store.GetCheckpointAsync());
    }

    [Fact(DisplayName = "T34 I09: a page that claims more data without advancing is a protocol error")]
    public async Task NonAdvancingPageRejected()
    {
        var server = await SeededServerAsync(1);
        var client = new TestReplica(server, "a");
        client.Transport.AfterPull = _ => Task.FromResult(new PullResult<Note>([], Checkpoint.Start, HasMore: true));

        await Assert.ThrowsAsync<SyncProtocolException>(() => client.Engine.PullAsync());
        Assert.Equal(1, client.Transport.PullCalls);
    }

    [Fact(DisplayName = "T34 I09: a page containing one document twice is rejected before applying")]
    public async Task DuplicateDocumentInPageRejected()
    {
        var server = await SeededServerAsync(1);
        var client = new TestReplica(server, "a");
        client.Transport.AfterPull = page => Task.FromResult(page with { Changes = [page.Changes[0], page.Changes[0] with { Version = 99 }] });

        await Assert.ThrowsAsync<SyncProtocolException>(() => client.Engine.PullAsync());
        Assert.Empty(await client.Engine.QueryAsync());
        Assert.True((await client.Store.GetCheckpointAsync()).IsStart);
    }

    [Fact(DisplayName = "T34 I09: an empty final page is accepted")]
    public async Task EmptyPage()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        var result = await client.Engine.PullAsync();
        Assert.Equal(0, result.Pulled);
        Assert.True(result.IsComplete);
    }

    [Fact(DisplayName = "T15 I09: a delayed older version never replaces a newer confirmed version")]
    public async Task OlderVersionIgnored()
    {
        var server = await SeededServerAsync(1);
        var writer = new TestReplica(server, "w2");
        await writer.Engine.SyncAsync();
        var client = new TestReplica(server, "a");
        await client.Engine.PullAsync();
        var v1 = await client.RecordAsync("n00");

        await writer.Engine.WriteAsync(new Note { Id = "n00", Title = "newer" });
        await writer.Engine.SyncAsync();
        await client.Engine.PullAsync();

        // Replay the old version under a fresh checkpoint.
        client.Transport.AfterPull = page => Task.FromResult(page with { Changes = [new RemoteChange<Note>(v1.Current, v1.BaseVersion!.Value)] });
        var replay = await client.Engine.PullAsync();

        Assert.Equal(0, replay.Pulled);
        Assert.Equal("newer", (await client.RecordAsync("n00")).Current.Title);
    }

    [Fact(DisplayName = "T35 I14: a checkpoint from another server epoch requires reset")]
    public async Task EpochMismatch()
    {
        var clock = new ManualClock(1_000);
        var first = await SeededServerAsync(1);
        var client = new TestReplica(first, "a");
        await client.Engine.PullAsync();
        var checkpoint = await client.Store.GetCheckpointAsync();

        var restored = InMemorySyncServerRef.Create(clock); // e.g. a restore that lost history
        Assert.Throws<SyncResetRequiredException>(() => restored.Server.Pull(new PullRequest(checkpoint, 10)));
    }

    [Fact(DisplayName = "T31 I10: tombstones propagate and stay queryable with includeDeleted")]
    public async Task TombstonesPropagate()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a");
        var b = new TestReplica(server, "b");
        await a.Engine.WriteAsync(new Note { Id = "n1" });
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        Assert.NotNull(await a.Engine.DeleteAsync("n1"));
        Assert.Null(await a.Engine.DeleteAsync("unknown"));
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        Assert.Empty(await b.Engine.QueryAsync());
        Assert.True((await b.Engine.QueryAsync(includeDeleted: true)).Single().Deleted);
    }

    [Fact(DisplayName = "T31 I11: delete versus concurrent update is resolved by the configured policy")]
    public async Task DeleteVersusUpdate()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a");
        var b = new TestReplica(server, "b", new Conflicts.ServerWinsConflictHandler<Note>());
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "v1" });
        await a.Engine.SyncAsync();
        await b.Engine.SyncAsync();

        await a.Engine.DeleteAsync("n1");
        await a.Engine.SyncAsync();
        await b.Engine.WriteAsync(new Note { Id = "n1", Title = "edited" });
        var result = await b.Engine.SyncAsync();

        Assert.Equal(1, result.Conflicts);
        Assert.True(server.Get("n1").Deleted);
        Assert.True((await b.RecordAsync("n1")).Current.Deleted);
    }
}
