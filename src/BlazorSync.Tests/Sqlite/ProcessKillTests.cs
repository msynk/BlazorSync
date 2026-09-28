using System.Diagnostics;
using BlazorSync.Clocks;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace BlazorSync.Tests.Sqlite;

/// <summary>
/// T16–T20, T51, I01, I03: a separate process writes to a SQLite replica and is terminated
/// (TerminateProcess on Windows, SIGKILL elsewhere) at an arbitrary point. Every write the process reported
/// as committed must be present, every transaction must be all-or-nothing, and the file must pass
/// <c>PRAGMA integrity_check</c>. This covers process termination, not power loss or OS crashes.
/// </summary>
public sealed class ProcessKillTests(ITestOutputHelper output) : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Theory(DisplayName = "T16 T17 T51 I01 I12: every acknowledged local write survives a kill, with its pending intent")]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(150)]
    public async Task LocalWritesSurviveKill(int killAfter)
    {
        var lines = await RunAndKillAsync("writes", killAfter, "100000");
        var acknowledged = lines.Select(l => l.Split(' ')).Select(p => (Index: int.Parse(p[1]), Stamp: HlcTimestamp.Parse(p[2]))).ToList();
        Assert.True(acknowledged.Count >= killAfter);

        var store = await _database.OpenAsync();
        foreach (var (index, stamp) in acknowledged)
        {
            var record = await store.GetAsync($"w{index:D6}");
            Assert.NotNull(record);
            Assert.True(record!.IsDirty);
            Assert.Equal(1, record.LocalRevision);
            Assert.Equal(stamp, record.Current.UpdatedAt);
        }

        // Writes committed but not yet reported may also exist; they must be complete records.
        var all = await store.QueryAsync();
        Assert.All(all, n => Assert.Equal(200, n.Title.Length));
        Assert.Equal(all.Count, await store.CountDirtyAsync());
        Assert.True(await store.GetClockHighWaterAsync() >= acknowledged.Max(a => a.Stamp));
        await AssertIntegrityAsync();
        output.WriteLine($"acknowledged {acknowledged.Count}, found {all.Count}");
    }

    [Theory(DisplayName = "T19 T20 I03: a page and its checkpoint are never split by a kill")]
    [InlineData(2)]
    [InlineData(25)]
    [InlineData(80)]
    public async Task PagesAreAtomicUnderKill(int killAfter)
    {
        const int size = 20;
        var lines = await RunAndKillAsync("pages", killAfter, "100000", size.ToString());
        var lastAcknowledged = lines.Select(l => int.Parse(l.Split(' ')[1])).Max();

        var store = await _database.OpenAsync();
        var cursor = await store.GetCursorAsync();
        var committed = int.Parse(cursor.Checkpoint.Value!["cp".Length..]);
        Assert.True(committed >= lastAcknowledged);

        var byBatch = (await store.QueryAsync())
            .GroupBy(n => int.Parse(n.Id[1..7]))
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(Enumerable.Range(0, committed + 1), byBatch.Keys.Order());
        Assert.All(byBatch.Values, count => Assert.Equal(size, count));
        await AssertIntegrityAsync();
        output.WriteLine($"acknowledged {lastAcknowledged}, committed {committed}");
    }

    private async Task<List<string>> RunAndKillAsync(string mode, int killAfter, params string[] extra)
    {
        var host = Path.Combine(AppContext.BaseDirectory, "BlazorSync.Tests.CrashHost.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { host, mode, _database.Path }.Concat(extra))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var lines = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (lines.Count < killAfter)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                Assert.Fail($"Host exited early: {await process.StandardError.ReadToEndAsync()}");
            }

            lines.Add(line);
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(timeout.Token);

        // Lines already buffered before the kill are also acknowledged commits.
        while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } rest)
        {
            if (rest.StartsWith("committed ", StringComparison.Ordinal))
            {
                lines.Add(rest);
            }
        }

        BlazorSync.Storage.Sqlite.SqliteStorePool.Release(_database.Path);
        return lines;
    }

    private async Task AssertIntegrityAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_database.Path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await command.ExecuteScalarAsync());
    }
}
