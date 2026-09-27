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
/// <item><description>server-wins and last-write-wins never invent timestamps nobody wrote.</description></item>
/// </list>
/// Failing seeds are reproducible from the test name.
/// </summary>
public sealed class RandomizedConvergenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string> Cases()
    {
        var data = new TheoryData<int, string>();
        foreach (var policy in new[] { "client-wins", "server-wins", "lww" })
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
    public async Task RandomSchedulesConverge(int seed, string policy)
    {
        var random = new Random(seed);
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
                switch (random.Next(10))
                {
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

            foreach (var replica in replicas)
            {
                results.Add(await replica.Engine.SyncAsync());
            }

            if (results.All(r => r.IsComplete && r.Pushed == 0 && r.Conflicts == 0))
            {
                break;
            }
        }

        var expected = server.Server.Snapshot().ToDictionary(n => n.Id, Describe);
        foreach (var replica in replicas)
        {
            Assert.Equal(0, await replica.Engine.CountDirtyAsync());
            var actual = (await replica.Engine.QueryAsync(includeDeleted: true)).ToDictionary(n => n.Id, Describe);
            Assert.Equal(expected, actual);
        }

        var final = server.Server.Snapshot().ToDictionary(n => n.Id, StringComparer.Ordinal);
        Assert.Equal(written.Keys.Order(StringComparer.Ordinal), final.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, stamps) in written)
        {
            if (policy == "lww")
            {
                Assert.True(final[id].UpdatedAt == stamps.Max(), $"seed {seed}: {id} ended at {final[id].UpdatedAt}, latest write was {stamps.Max()}");
            }

            if (policy is "lww" or "server-wins")
            {
                Assert.True(stamps.Contains(final[id].UpdatedAt), $"seed {seed}: {id} ended on a timestamp nobody wrote");
            }
        }

        var sent = replicas.SelectMany(r => r.Transport.PushLog).SelectMany(p => p.Operations).Select(o => o.OperationId).Distinct().Count();
        Assert.Equal(sent, server.Server.ReceiptCount);
        output.WriteLine($"seed {seed} {policy}: {server.Server.Snapshot().Count} documents, {sent} operations");
    }

    private static string Describe(Note n) => $"{n.Title}|{n.Deleted}|{n.UpdatedAt}";

    private static IConflictHandler<Note> Handler(string policy) => policy switch
    {
        "client-wins" => new ClientWinsConflictHandler<Note>(),
        "server-wins" => new ServerWinsConflictHandler<Note>(),
        _ => new LastWriteWinsConflictHandler<Note>(),
    };

    private sealed class SkewedClock(ManualClock world, long offset) : Clocks.IPhysicalClock
    {
        public long NowMilliseconds() => world.NowMilliseconds() + offset;
    }
}
