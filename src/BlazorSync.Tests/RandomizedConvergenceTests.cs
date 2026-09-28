using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace BlazorSync.Tests;

/// <summary>
/// T60 / I20: seeded random schedules of local writes, deletes, partial syncs, lost responses and
/// crashes before acknowledgement, and edits made while a push is in flight, followed by quiescence.
/// Every replica must converge to the server state with nothing left dirty, the server must never apply
/// one operation id twice, and the result must match a reference model of the policy:
/// <list type="bullet">
/// <item><description>last-write-wins ends on the write with the greatest authoring timestamp;</description></item>
/// <item><description>server-wins, last-write-wins and the default (defer) never invent timestamps nobody wrote;</description></item>
/// <item><description>with the default policy, conflicts are kept until the schedule (or quiescence) resolves or
/// discards them, and no replica is left with a kept conflict.</description></item>
/// </list>
/// Failing seeds are reproducible from the test name.
/// </summary>
public sealed class RandomizedConvergenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string> Cases()
    {
        var data = new TheoryData<int, string>();
        foreach (var policy in new[] { "client-wins", "server-wins", "lww", "defer" })
        {
            for (var seed = 1; seed <= 15; seed++)
            {
                data.Add(seed, policy);
            }
        }

        return data;
    }

    [Theory(DisplayName = "T60 I20: random schedules converge after quiescence")]
    [MemberData(nameof(Cases))]
    public Task RandomSchedulesConverge(int seed, string policy) => RunAsync(seed, policy, restores: false);

    [Theory(DisplayName = "T35 T60 I14 I20: random schedules with authority restores converge after quiescence")]
    [MemberData(nameof(Cases))]
    public Task RandomSchedulesWithRestoresConverge(int seed, string policy) => RunAsync(seed, policy, restores: true);

    private async Task RunAsync(int seed, string policy, bool restores)
    {
        var random = new Random(seed);
        Server.InMemorySyncServerBackup<Note>? backup = null;
        var restoreCount = 0;
        var settledTotal = 0;
        var world = new ManualClock(1_000_000);
        var server = InMemorySyncServerRef.Create(world);
        var replicas = Enumerable.Range(0, 3)
            .Select(i => new TestReplica(
                server,
                $"r{i}",
                Handler(policy),
                new SyncOptions<Note> { PushBatchSize = random.Next(1, 5), PullBatchSize = random.Next(1, 6) },
                new SkewedClock(world, random.Next(-50, 50))))
            .ToList();
        var written = new Dictionary<string, List<HlcTimestamp>>(StringComparer.Ordinal);
        void Record(LocalWriteReceipt? receipt)
        {
            if (receipt is { } r)
            {
                (written.TryGetValue(r.Id, out var list) ? list : written[r.Id] = []).Add(r.UpdatedAt);
            }
        }
        var ids = Enumerable.Range(0, 6).Select(i => $"doc{i}").ToArray();

        for (var step = 0; step < 250; step++)
        {
            world.Advance(random.Next(0, 3));
            var replica = replicas[random.Next(replicas.Count)];
            var id = ids[random.Next(ids.Length)];
            try
            {
                switch (random.Next(restores ? 12 : 10))
                {
                    case 10:
                        backup = server.Server.CreateBackup();
                        break;
                    case 11 when backup is not null:
                        server.Restore(backup, world);
                        restoreCount++;
                        break;
                    case 0 or 1:
                        Record(await replica.Engine.WriteAsync(new Note { Id = id, Title = $"s{seed}-{step}" }));
                        break;
                    case 2:
                        // A local edit lands while the push is waiting for its response.
                        var inFlightId = ids[random.Next(ids.Length)];
                        replica.Transport.AfterPush = async result =>
                        {
                            replica.Transport.AfterPush = null;
                            Record(await replica.Engine.WriteAsync(new Note { Id = inFlightId, Title = $"s{seed}-{step}-flight" }));
                            return result;
                        };
                        await replica.Engine.PushAsync();
                        break;
                    case 3:
                        Record(await replica.Engine.DeleteAsync(id));
                        break;
                    case 4:
                        replica.Transport.LoseResponses = 1;
                        await replica.Engine.PushAsync();
                        break;
                    case 5:
                        await replica.Engine.PullAsync();
                        break;
                    case 6:
                        await replica.Engine.PushAsync();
                        break;
                    case 8 when policy == "defer":
                        Record(await SettleOneAsync(replica, random, seed, step));
                        break;
                    case 9 when step % 2 == 0:
                        // A dependency group of two documents.
                        var second = ids[(Array.IndexOf(ids, id) + 1 + random.Next(ids.Length - 1)) % ids.Length];
                        foreach (var receipt in await replica.Engine.WriteGroupAsync([new Note { Id = id, Title = $"s{seed}-{step}-g" }, new Note { Id = second, Title = $"s{seed}-{step}-g" }]))
                        {
                            Record(receipt);
                        }

                        break;
                    case 7:
                        // Crash before the acknowledgement of the next push commits.
                        var target = replica.Store.UpdateCalls + 2;
                        replica.Store.BeforeUpdate = (call, _, _) => call == target ? throw new InjectedFaultException("crash") : Task.CompletedTask;
                        await replica.Engine.PushAsync();
                        break;
                    default:
                        await replica.Engine.SyncAsync();
                        break;
                }
            }
            catch (InjectedFaultException)
            {
                // Expected: the next round retries.
            }
            finally
            {
                replica.Store.BeforeUpdate = null;
                replica.Transport.LoseResponses = 0;
                replica.Transport.AfterPush = null;
            }
        }

        // Quiescence: no new writes, reliable network, sync until every replica reports completion twice.
        for (var round = 0; ; round++)
        {
            Assert.True(round < 20, $"seed {seed} {policy} did not quiesce");
            var results = new List<SyncResult>();
            foreach (var replica in replicas)
            {
                results.Add(await replica.Engine.SyncAsync());
            }

            // Changes parked because another change of their group was kept as a conflict are released when it is resolved;
            // a group whose conflict was discarded or settled with the server state is retried explicitly.
            foreach (var replica in replicas)
            {
                foreach (var parked in await replica.Engine.GetRejectedAsync(100))
                {
                    Record(await replica.Engine.RetryRejectedAsync(parked.Current.Id));
                }
            }

            // The user decides every kept conflict (default policy only).
            var settled = 0;
            foreach (var replica in replicas)
            {
                while ((await replica.Engine.GetConflictsAsync(1)).Count > 0)
                {
                    Assert.True(settled < 100, $"seed {seed}: conflicts are not being settled");
                    Record(await SettleOneAsync(replica, random, seed, 1000 + round));
                    settled++;
                }
            }

            foreach (var replica in replicas)
            {
                results.Add(await replica.Engine.SyncAsync());
            }

            settledTotal += settled;
            if (settled == 0 && results.All(r => r.IsComplete && r.Pushed == 0 && r.Conflicts == 0))
            {
                break;
            }
        }

        var expected = server.Server.Snapshot().ToDictionary(n => n.Id, Describe);
        foreach (var replica in replicas)
        {
            Assert.Equal(0, await replica.Engine.CountDirtyAsync());
            Assert.Empty(await replica.Engine.GetConflictsAsync());
            var actual = (await replica.Engine.QueryAsync(includeDeleted: true)).ToDictionary(n => n.Id, Describe);
            Assert.Equal(expected, actual);
        }

        var final = server.Server.Snapshot().ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (restoreCount == 0)
        {
            Assert.Equal(written.Keys.Order(StringComparer.Ordinal), final.Keys.Order(StringComparer.Ordinal));
        }
        else
        {
            // A restore may lose documents whose only copies were clean; it never invents documents.
            Assert.Subset(written.Keys.ToHashSet(StringComparer.Ordinal), final.Keys.ToHashSet(StringComparer.Ordinal));
        }

        foreach (var (id, stamps) in written)
        {
            if (!final.ContainsKey(id))
            {
                continue;
            }

            // A restore can discard the latest write, so the maximum is only checked without restores.
            if (policy == "lww" && restoreCount == 0)
            {
                Assert.True(final[id].UpdatedAt == stamps.Max(), $"seed {seed}: {id} ended at {final[id].UpdatedAt}, latest write was {stamps.Max()}");
            }

            if (policy is "lww" or "server-wins" or "defer")
            {
                Assert.True(stamps.Contains(final[id].UpdatedAt), $"seed {seed}: {id} ended on a timestamp nobody wrote");
            }
        }

        var operations = replicas.SelectMany(r => r.Transport.PushLog).SelectMany(p => p.Operations).ToList();
        var sent = operations.Select(o => o.OperationId).Distinct().Count();
        if (restoreCount == 0)
        {
            // Every ungrouped operation is decided exactly once. A grouped one may be aborted with its group and replaced
            // by a new operation later, so it is decided at most once.
            var ungrouped = operations.Where(o => o.Group is null).Select(o => o.OperationId).Distinct().Count();
            Assert.InRange(server.Server.ReceiptCount, ungrouped, sent);
        }

        output.WriteLine($"seed {seed} {policy} restores={restoreCount} settled at quiescence={settledTotal}: {server.Server.Snapshot().Count} documents, {sent} operations");
    }

    /// <summary>Resolves (keeping the local change, re-stamped) or discards one kept conflict, if any.</summary>
    private static async Task<LocalWriteReceipt?> SettleOneAsync(TestReplica replica, Random random, int seed, int step)
    {
        var conflicts = await replica.Engine.GetConflictsAsync(10);
        if (conflicts.Count == 0)
        {
            return null;
        }

        var record = conflicts[random.Next(conflicts.Count)];
        if (random.Next(2) == 0)
        {
            await replica.Engine.DiscardConflictAsync(record.Current.Id);
            return null;
        }

        var resolved = record.Conflict!.Local;
        resolved.Title = $"s{seed}-{step}-resolved";
        return await replica.Engine.ResolveConflictAsync(record.Current.Id, resolved);
    }

    private static string Describe(Note n) => $"{n.Title}|{n.Deleted}|{n.UpdatedAt}";

    private static IConflictHandler<Note> Handler(string policy) => policy switch
    {
        "client-wins" => new ClientWinsConflictHandler<Note>(),
        "server-wins" => new ServerWinsConflictHandler<Note>(),
        "defer" => new DeferConflictHandler<Note>(),
        _ => new LastWriteWinsConflictHandler<Note>(),
    };

    private sealed class SkewedClock(ManualClock world, long offset) : Clocks.IPhysicalClock
    {
        public long NowMilliseconds() => world.NowMilliseconds() + offset;
    }
}
