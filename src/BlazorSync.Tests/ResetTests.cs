using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests;

/// <summary>
/// T33/T35 I10 I14: the authority is restored from a backup (new epoch, lost history). Replicas must reset,
/// resnapshot, keep pending edits and mark records the new history does not contain as missing.
/// </summary>
public sealed class ResetTests
{
    [Fact(DisplayName = "T35 I14: after a restore a clean replica adopts the restored (older) state")]
    public async Task CleanReplicaAdoptsRestoredState()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a");
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "kept" });
        await a.Engine.SyncAsync();
        var backup = server.Server.CreateBackup();
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "lost" });
        await a.Engine.SyncAsync();

        server.Restore(backup);
        var result = await a.Engine.SyncAsync();

        Assert.True(result.ResetPerformed);
        Assert.True(result.IsComplete);
        var record = await a.RecordAsync("n1");
        Assert.Equal("kept", record.Current.Title);
        Assert.False(record.IsDirty);
        Assert.Equal(1, record.Generation);
        Assert.Equal(1, (await a.Store.GetCursorAsync()).Generation);
        Assert.False((await a.Store.GetCursorAsync()).Resnapshot);
    }

    [Fact(DisplayName = "T33 I10 I14: clean records missing from the restored history are marked, hidden and not deleted on the server")]
    public async Task MissingRecordsAreMarked()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a");
        await a.Engine.WriteAsync(new Note { Id = "old" });
        await a.Engine.SyncAsync();
        var backup = server.Server.CreateBackup();
        await a.Engine.WriteAsync(new Note { Id = "created-after-backup" });
        await a.Engine.SyncAsync();

        server.Restore(backup);
        var result = await a.Engine.SyncAsync();

        Assert.Equal(1, result.MissingAfterReset);
        Assert.Equal(["old"], (await a.Engine.QueryAsync(includeDeleted: true)).Select(n => n.Id));
        var missing = await a.RecordAsync("created-after-backup");
        Assert.True(missing.MissingAfterReset);
        Assert.False(missing.IsDirty);
        Assert.Equal(0, result.Pushed); // not resurrected on the server
        Assert.Null(server.Server.GetVersion("created-after-backup"));
    }

    [Fact(DisplayName = "I10 I14: an unknown reset reason is handled like an epoch reset: records are hidden, never removed")]
    public async Task UnknownReasonHides()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a", transport: inner => new FutureReasonTransport(inner));
        await a.Engine.WriteAsync(new Note { Id = "old" });
        await a.Engine.SyncAsync();
        var backup = server.Server.CreateBackup();
        await a.Engine.WriteAsync(new Note { Id = "created-after-backup" });
        await a.Engine.SyncAsync();

        server.Restore(backup);
        var result = await a.Engine.SyncAsync();

        Assert.True(result.ResetPerformed);
        Assert.Equal((1, 0), (result.MissingAfterReset, result.PurgedAfterReset));
        Assert.True((await a.RecordAsync("created-after-backup")).MissingAfterReset);
    }

    /// <summary>Rewrites every reset reason to one this client does not know.</summary>
    private sealed class FutureReasonTransport(Transport.ISyncTransport<Note> inner) : Transport.ISyncTransport<Note>
    {
        public async Task<Protocol.PullResult<Note>> PullAsync(Protocol.PullRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.PullAsync(request, cancellationToken);
            }
            catch (SyncResetRequiredException error)
            {
                throw new SyncResetRequiredException(error.Message, "some-future-reason");
            }
        }

        public Task<Protocol.PushResult<Note>> PushAsync(Protocol.PushRequest<Note> request, CancellationToken cancellationToken = default) =>
            inner.PushAsync(request, cancellationToken);

        public IAsyncEnumerable<Protocol.StreamEvent<Note>> StreamAsync(Checkpoint since, CancellationToken cancellationToken = default) =>
            inner.StreamAsync(since, cancellationToken);
    }

    [Fact(DisplayName = "T35 I14: a pending edit survives a reset and is pushed")]
    public async Task PendingEditSurvives()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a");
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "v1" });
        await a.Engine.SyncAsync();
        var backup = server.Server.CreateBackup();
        await a.Engine.WriteAsync(new Note { Id = "offline", Title = "new" });
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "edited offline" });

        server.Restore(backup);
        var result = await a.Engine.SyncAsync();

        Assert.True(result.ResetPerformed);
        Assert.Equal(2, result.Pushed);
        Assert.Equal("edited offline", server.Get("n1").Title);
        Assert.Equal("new", server.Get("offline").Title);
        Assert.Equal(0, await a.Engine.CountDirtyAsync());
    }

    [Fact(DisplayName = "T35 I11 I14: an edit based on a version the restore lost goes through conflict resolution")]
    public async Task EditOnLostVersionConflicts()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a", new Conflicts.ServerWinsConflictHandler<Note>());
        var b = new TestReplica(server, "b");
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "v1" });
        await a.Engine.SyncAsync();
        var backup = server.Server.CreateBackup();
        await b.Engine.SyncAsync();
        await b.Engine.WriteAsync(new Note { Id = "n1", Title = "lost" });
        await b.Engine.SyncAsync();
        await a.Engine.SyncAsync();
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "based on lost" });

        server.Restore(backup);
        var result = await a.Engine.SyncAsync();

        // The base version names a state the restored server never had, so it cannot silently overwrite.
        Assert.Equal(1, result.Conflicts);
        Assert.Equal("v1", server.Get("n1").Title);
        Assert.Equal("v1", (await a.RecordAsync("n1")).Current.Title);
    }

    [Fact(DisplayName = "T11 T35 I04: an operation committed before the backup is replayed, not applied twice")]
    public async Task ReceiptSurvivesRestore()
    {
        var server = InMemorySyncServerRef.Create();
        var merges = 0;
        var a = new TestReplica(server, "a", new Conflicts.DelegateConflictHandler<Note>(c => { merges++; return Conflicts.ConflictResolution<Note>.KeepFork(); }));
        await a.Engine.WriteAsync(new Note { Id = "n1", Title = "once" });
        a.Transport.LoseResponses = 1;
        await Assert.ThrowsAsync<InjectedFaultException>(() => a.Engine.SyncAsync());
        var backup = server.Server.CreateBackup();

        server.Restore(backup);
        await a.Engine.PullAsync(); // establishes a checkpoint on the new epoch first
        var result = await a.Engine.SyncAsync();

        Assert.Equal(0, merges);
        Assert.True(result.IsComplete);
        Assert.Equal(1, server.Server.ReceiptCount);
        Assert.False((await a.RecordAsync("n1")).IsDirty);
    }

    [Fact(DisplayName = "T19 I14: a crash during the resnapshot resumes and completes")]
    public async Task CrashDuringResnapshot()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a", options: new SyncOptions<Note> { PullBatchSize = 2 });
        for (var i = 0; i < 5; i++)
        {
            await a.Engine.WriteAsync(new Note { Id = $"n{i}", Title = "kept" });
        }

        await a.Engine.SyncAsync();
        var backup = server.Server.CreateBackup();
        for (var i = 0; i < 5; i++)
        {
            await a.Engine.WriteAsync(new Note { Id = $"n{i}", Title = "lost" });
            await a.Engine.WriteAsync(new Note { Id = $"extra{i}" });
        }

        await a.Engine.SyncAsync();
        server.Restore(backup);

        // Crash on the second snapshot page, then again during the missing-record sweep.
        var pages = 0;
        a.Store.BeforeUpdate = (_, updates, cursor) =>
            cursor is { Resnapshot: true } && updates.Count > 0 && ++pages == 2
                ? throw new InjectedFaultException("crash mid-snapshot")
                : Task.CompletedTask;
        await Assert.ThrowsAsync<InjectedFaultException>(() => a.Engine.SyncAsync());
        Assert.True((await a.Store.GetCursorAsync()).Resnapshot);

        a.Store.BeforeUpdate = (_, updates, cursor) =>
            cursor is null && updates.Count > 0 && updates.All(u => u.Id.StartsWith("extra", StringComparison.Ordinal))
                ? throw new InjectedFaultException("crash mid-sweep")
                : Task.CompletedTask;
        await Assert.ThrowsAsync<InjectedFaultException>(() => a.Engine.SyncAsync());
        Assert.True((await a.Store.GetCursorAsync()).Resnapshot);

        a.Store.BeforeUpdate = null;
        var result = await a.Engine.SyncAsync();

        Assert.True(result.IsComplete);
        Assert.False((await a.Store.GetCursorAsync()).Resnapshot);
        Assert.Equal(5, result.MissingAfterReset);
        Assert.All(await a.Engine.QueryAsync(), n => Assert.Equal("kept", n.Title));
        Assert.Equal(5, (await a.Engine.QueryAsync()).Count);
    }

    [Fact(DisplayName = "T37 I10: a missing record reappears when the server has it again, or when written locally")]
    public async Task MissingRecordReappears()
    {
        var server = InMemorySyncServerRef.Create();
        var a = new TestReplica(server, "a");
        var b = new TestReplica(server, "b");
        var backup = server.Server.CreateBackup();
        await a.Engine.WriteAsync(new Note { Id = "x", Title = "lost" });
        await a.Engine.WriteAsync(new Note { Id = "y", Title = "lost" });
        await a.Engine.SyncAsync();

        server.Restore(backup);
        await a.Engine.SyncAsync();
        Assert.True((await a.RecordAsync("x")).MissingAfterReset);

        await b.Engine.WriteAsync(new Note { Id = "x", Title = "recreated elsewhere" });
        await b.Engine.SyncAsync();
        await a.Engine.WriteAsync(new Note { Id = "y", Title = "rewritten locally" });
        await a.Engine.SyncAsync();

        Assert.False((await a.RecordAsync("x")).MissingAfterReset);
        Assert.Equal("recreated elsewhere", (await a.RecordAsync("x")).Current.Title);
        Assert.False((await a.RecordAsync("y")).MissingAfterReset);
        Assert.Equal("rewritten locally", server.Get("y").Title);
        Assert.Equal(2, (await a.Engine.QueryAsync()).Count);
    }

    [Fact(DisplayName = "I14: a reset required for a start checkpoint is not retried")]
    public async Task ResetAtStartIsNotRetried()
    {
        var client = new TestReplica(InMemorySyncServerRef.Create(), "a");
        client.Transport.AfterPull = _ => throw new SyncResetRequiredException("always");

        await Assert.ThrowsAsync<SyncResetRequiredException>(() => client.Engine.PullAsync());
        Assert.Equal(0, (await client.Store.GetCursorAsync()).Generation);
    }

    [Fact(DisplayName = "I14 I17: a checkpoint stored by an older server version resets the replica; a malformed one is a protocol error")]
    public async Task LegacyCheckpointResets()
    {
        var server = InMemorySyncServerRef.Create();
        await new TestReplica(server, "writer").Engine.WriteAsync(new Note { Id = "n1" });
        var client = new TestReplica(server, "a");
        await client.Store.UpdateAsync([], new Storage.ReplicaCursor(new Checkpoint($"{server.Server.Epoch}:0"), 0, false));

        var result = await client.Engine.SyncAsync();

        Assert.True(result.ResetPerformed);
        Assert.Equal(1, (await client.Store.GetCursorAsync()).Generation);
        var garbage = new Server.InMemorySyncServer<Note>(NoteJson.ServerOptions());
        await Assert.ThrowsAsync<SyncProtocolException>(() => garbage.PullAsync(Server.SyncCallContext.Anonymous, new Protocol.PullRequest(new Checkpoint("no-position"), 10)));
    }
}
