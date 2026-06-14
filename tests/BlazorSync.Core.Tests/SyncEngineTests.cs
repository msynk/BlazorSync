using BlazorSync.Core.Clocks;
using BlazorSync.Core.Conflicts;
using BlazorSync.Core.Server;
using BlazorSync.Core.Storage;
using BlazorSync.Core.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Core.Tests;

public sealed class SyncEngineTests
{
    private static SyncEngine<Note> CreateClient(
        InMemorySyncServer<Note> server,
        string node,
        IConflictHandler<Note>? conflictHandler = null)
    {
        var store = new InMemoryLocalStore<Note>();
        var transport = new InProcessTransport<Note>(server);
        var clock = new HybridLogicalClock(node);
        return new SyncEngine<Note>(store, transport, clock, conflictHandler);
    }

    [Fact]
    public async Task LocalInsert_SyncsToServer()
    {
        var server = new InMemorySyncServer<Note>();
        var client = CreateClient(server, "client-a");

        await client.WriteAsync(new Note { Id = "n1", Title = "Hello" });
        var result = await client.SyncAsync();

        Assert.Equal(1, result.Pushed);
        var master = server.Snapshot();
        Assert.Single(master);
        Assert.Equal("Hello", master[0].Title);
    }

    [Fact]
    public async Task TwoClients_Converge()
    {
        var server = new InMemorySyncServer<Note>();
        var clientA = CreateClient(server, "client-a");
        var clientB = CreateClient(server, "client-b");

        await clientA.WriteAsync(new Note { Id = "n1", Title = "from A" });
        await clientA.SyncAsync();

        await clientB.SyncAsync();

        var notesOnB = await clientB.QueryAsync();
        Assert.Single(notesOnB);
        Assert.Equal("from A", notesOnB[0].Title);
    }

    [Fact]
    public async Task Update_PropagatesToOtherClient()
    {
        var server = new InMemorySyncServer<Note>();
        var clientA = CreateClient(server, "client-a");
        var clientB = CreateClient(server, "client-b");

        await clientA.WriteAsync(new Note { Id = "n1", Title = "v1" });
        await clientA.SyncAsync();
        await clientB.SyncAsync();

        // Update on A, sync both ways.
        await clientA.WriteAsync(new Note { Id = "n1", Title = "v2" });
        await clientA.SyncAsync();
        await clientB.SyncAsync();

        var notesOnB = await clientB.QueryAsync();
        Assert.Single(notesOnB);
        Assert.Equal("v2", notesOnB[0].Title);
    }

    [Fact]
    public async Task SoftDelete_PropagatesAndIsHiddenFromQueries()
    {
        var server = new InMemorySyncServer<Note>();
        var clientA = CreateClient(server, "client-a");
        var clientB = CreateClient(server, "client-b");

        await clientA.WriteAsync(new Note { Id = "n1", Title = "doomed" });
        await clientA.SyncAsync();
        await clientB.SyncAsync();

        await clientA.DeleteAsync("n1");
        await clientA.SyncAsync();
        await clientB.SyncAsync();

        Assert.Empty(await clientB.QueryAsync());
        Assert.Single(await clientB.QueryAsync(includeDeleted: true));
    }

    [Fact]
    public async Task ConcurrentEdit_ClientWins_LateClientOverwritesServer()
    {
        var server = new InMemorySyncServer<Note>();
        var clientA = CreateClient(server, "client-a"); // default = client-wins
        var clientB = CreateClient(server, "client-b");

        // Both start from the same synced baseline.
        await clientA.WriteAsync(new Note { Id = "n1", Title = "base" });
        await clientA.SyncAsync();
        await clientB.SyncAsync();

        // Both edit offline.
        await clientA.WriteAsync(new Note { Id = "n1", Title = "A edit" });
        await clientB.WriteAsync(new Note { Id = "n1", Title = "B edit" });

        // A pushes first and wins the race to the server.
        await clientA.SyncAsync();

        // B syncs: its push conflicts, client-wins re-pushes B's version over the server's.
        await clientB.SyncAsync();

        Assert.Equal("B edit", server.Snapshot()[0].Title);

        // A converges to B's value on its next pull.
        await clientA.SyncAsync();
        var notesOnA = await clientA.QueryAsync();
        Assert.Equal("B edit", notesOnA[0].Title);
    }

    [Fact]
    public async Task ConcurrentEdit_ServerWins_DiscardsLateClientChange()
    {
        var server = new InMemorySyncServer<Note>();
        var clientA = CreateClient(server, "client-a");
        var clientB = CreateClient(server, "client-b", new ServerWinsConflictHandler<Note>());

        await clientA.WriteAsync(new Note { Id = "n1", Title = "base" });
        await clientA.SyncAsync();
        await clientB.SyncAsync();

        await clientA.WriteAsync(new Note { Id = "n1", Title = "A edit" });
        await clientB.WriteAsync(new Note { Id = "n1", Title = "B edit" });

        await clientA.SyncAsync(); // A wins the server
        await clientB.SyncAsync(); // B's conflict resolves to server-wins

        Assert.Equal("A edit", server.Snapshot()[0].Title);
        var notesOnB = await clientB.QueryAsync();
        Assert.Equal("A edit", notesOnB[0].Title);
    }

    [Fact]
    public async Task NoSpuriousConflict_WhenOwnWriteEchoesBackThroughPull()
    {
        var server = new InMemorySyncServer<Note>();
        var client = CreateClient(server, "client-a");

        await client.WriteAsync(new Note { Id = "n1", Title = "hello" });
        await client.SyncAsync();

        // A second sync pulls the server's echo of our own write. It must not be seen as a conflict
        // and the record must end up clean (not dirty).
        var result = await client.SyncAsync();

        Assert.Equal(0, result.Conflicts);
        var record = await client.GetAsync("n1");
        Assert.NotNull(record);
        Assert.False(record!.IsDirty);
    }

    [Fact]
    public async Task RepeatedSync_IsIdempotent()
    {
        var server = new InMemorySyncServer<Note>();
        var client = CreateClient(server, "client-a");

        await client.WriteAsync(new Note { Id = "n1", Title = "x" });
        await client.SyncAsync();
        await client.SyncAsync();
        await client.SyncAsync();

        Assert.Single(server.Snapshot());
        Assert.Single(await client.QueryAsync());
    }
}
