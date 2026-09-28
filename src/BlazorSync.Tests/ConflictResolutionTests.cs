using BlazorSync.Conflicts;
using BlazorSync.Storage;
using BlazorSync.Tests.Sqlite;
using BlazorSync.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BlazorSync.Tests;

/// <summary>ADR-006, I11: conflicts are kept durably by default and resolved or discarded explicitly.</summary>
public sealed class ConflictResolutionTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact(DisplayName = "I11: by default a conflict is kept: the replica shows the server state and keeps the local change")]
    public async Task DefaultKeepsConflict()
    {
        var server = InMemorySyncServerRef.Create();
        var other = new TestReplica(server, "other");
        var client = new TestReplica(server, "client"); // default policy
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "base" });
        await other.Engine.SyncAsync();
        await client.Engine.SyncAsync();

        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "theirs" });
        await other.Engine.SyncAsync();
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "mine" });
        var result = await client.Engine.SyncAsync();

        Assert.Equal(1, result.Conflicts);
        Assert.True(result.IsComplete); // nothing left to push: the local change waits for a decision
        var record = await client.RecordAsync("n1");
        Assert.Equal("theirs", record.Current.Title);
        Assert.False(record.IsDirty);
        Assert.Equal("mine", record.Conflict!.Local.Title);
        Assert.Equal("theirs", record.Conflict.Server.Title);
        Assert.Equal("base", record.Conflict.Base!.Title);
        Assert.Equal("theirs", server.Get("n1").Title);
        Assert.Single(await client.Engine.GetConflictsAsync());
    }

    [Fact(DisplayName = "I11 I19: resolving a kept conflict pushes the resolution against the latest server version; unrelated work never waits")]
    public async Task ResolveAndUnrelatedProgress()
    {
        var server = InMemorySyncServerRef.Create();
        var other = new TestReplica(server, "other");
        var client = new TestReplica(server, "client");
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "base" });
        await other.Engine.SyncAsync();
        await client.Engine.SyncAsync();
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "theirs" });
        await other.Engine.SyncAsync();
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "mine" });
        await client.Engine.WriteAsync(new Note { Id = "unrelated", Title = "independent" });
        await client.Engine.SyncAsync();
        Assert.Equal("independent", server.Get("unrelated").Title);

        // The server moves on before the user decides; the resolution is based on the newest state.
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "theirs again" });
        await other.Engine.SyncAsync();
        await client.Engine.SyncAsync();
        Assert.Equal("theirs again", (await client.RecordAsync("n1")).Current.Title);
        Assert.NotNull((await client.RecordAsync("n1")).Conflict);

        Assert.NotNull(await client.Engine.ResolveConflictAsync("n1", new Note { Id = "n1", Title = "merged by user" }));
        var result = await client.Engine.SyncAsync();

        Assert.Equal(0, result.Conflicts);
        Assert.Equal("merged by user", server.Get("n1").Title);
        Assert.Empty(await client.Engine.GetConflictsAsync());
        Assert.Null(await client.Engine.ResolveConflictAsync("n1", new Note { Id = "n1" }));
    }

    [Fact(DisplayName = "I11: discarding a kept conflict keeps the server state")]
    public async Task Discard()
    {
        var server = InMemorySyncServerRef.Create();
        var other = new TestReplica(server, "other");
        var client = new TestReplica(server, "client");
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "theirs" });
        await other.Engine.SyncAsync();
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "same id, created offline" });
        await client.Engine.SyncAsync();

        Assert.True(await client.Engine.DiscardConflictAsync("n1"));
        Assert.False(await client.Engine.DiscardConflictAsync("n1"));
        Assert.Empty(await client.Engine.GetConflictsAsync());
        Assert.Equal("theirs", (await client.RecordAsync("n1")).Current.Title);
        Assert.Equal(0, (await client.Engine.SyncAsync()).Pushed);
    }

    [Fact(DisplayName = "I01 I11: a kept conflict survives a restart (SQLite) and can be resolved afterwards")]
    public async Task ConflictSurvivesRestart()
    {
        var server = InMemorySyncServerRef.Create();
        var other = new TestReplica(server, "other");
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "base" });
        await other.Engine.SyncAsync();
        var first = new TestReplica(server, "client", store: await _database.OpenAsync());
        await first.Engine.SyncAsync();
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "theirs" });
        await other.Engine.SyncAsync();
        await first.Engine.WriteAsync(new Note { Id = "n1", Title = "mine" });
        await first.Engine.SyncAsync();

        BlazorSync.Storage.Sqlite.SqliteStorePool.Release(_database.Path);
        var restarted = new TestReplica(server, "client", store: await _database.OpenAsync());
        var conflict = (await restarted.Engine.GetConflictsAsync()).Single().Conflict!;
        Assert.Equal(("mine", "theirs", "base"), (conflict.Local.Title, conflict.Server.Title, conflict.Base!.Title));

        await restarted.Engine.ResolveConflictAsync("n1", new Note { Id = "n1", Title = $"{conflict.Server.Title}+{conflict.Local.Title}" });
        await restarted.Engine.SyncAsync();
        Assert.Equal("theirs+mine", server.Get("n1").Title);
    }

    [Fact(DisplayName = "I17: a SQLite schema 1 database with a pending write upgrades to schema 2 without losing it")]
    public async Task SqliteMigrationKeepsPendingWork()
    {
        await using (var connection = new SqliteConnection($"Data Source={_database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE bs_meta (collection TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (collection, key)) WITHOUT ROWID;
                CREATE TABLE bs_records (
                    collection TEXT NOT NULL, id TEXT NOT NULL, id_key BLOB NOT NULL, current TEXT NOT NULL, updated_at TEXT NOT NULL,
                    deleted INTEGER NOT NULL, base TEXT, base_version INTEGER, is_dirty INTEGER NOT NULL, local_revision INTEGER NOT NULL,
                    pending_id TEXT, pending_revision INTEGER, pending_base_version INTEGER, pending_payload TEXT,
                    rejection_revision INTEGER, rejection_code TEXT, rejection_message TEXT, observed TEXT, observed_version INTEGER,
                    generation INTEGER NOT NULL, missing INTEGER NOT NULL, PRIMARY KEY (collection, id));
                CREATE INDEX bs_records_pending ON bs_records (collection, updated_at, id_key) WHERE is_dirty = 1 AND rejection_code IS NULL;
                CREATE INDEX bs_records_dirty ON bs_records (collection) WHERE is_dirty = 1;
                CREATE INDEX bs_records_stale ON bs_records (collection, id_key) WHERE is_dirty = 0 AND missing = 0;
                CREATE INDEX bs_records_visible ON bs_records (collection, id_key) WHERE missing = 0;
                INSERT INTO bs_meta VALUES ('', 'replica_id', 'r1'), ('', 'incarnation', 'i1');
                INSERT INTO bs_records VALUES ('notes', 'n1', X'006E0031',
                    '{"Id":"n1","UpdatedAt":"000000000001000:000000:client","Deleted":false,"Title":"written by version 1","Body":""}',
                    '000000000001000:000000:client', 0, NULL, NULL, 1, 1,
                    'op-from-v1', 1, NULL, '{"Id":"n1","UpdatedAt":"000000000001000:000000:client","Deleted":false,"Title":"written by version 1","Body":""}',
                    NULL, NULL, NULL, NULL, NULL, 0, 0);
                PRAGMA user_version = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }

        BlazorSync.Storage.Sqlite.SqliteStorePool.Release(_database.Path);
        var store = await _database.OpenAsync();
        var record = (await store.GetAsync("n1"))!;
        Assert.True(record.IsDirty);
        Assert.Equal("op-from-v1", record.Pending!.OperationId);
        Assert.Null(record.Conflict);

        var server = InMemorySyncServerRef.Create();
        var client = new TestReplica(server, "client", store: store);
        await client.Engine.SyncAsync();
        Assert.Equal("written by version 1", server.Get("n1").Title);
        Assert.Equal("op-from-v1", client.Transport.PushLog.Single().Operations.Single().OperationId); // same identity after migration
        await using var check = new SqliteConnection($"Data Source={_database.Path}");
        await check.OpenAsync();
        await using var version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, await version.ExecuteScalarAsync());
    }
}
