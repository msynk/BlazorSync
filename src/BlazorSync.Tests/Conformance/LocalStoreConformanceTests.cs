using BlazorSync.Clocks;
using BlazorSync.Storage;
using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests.Conformance;

/// <summary>
/// The behaviour every <see cref="ILocalStore{TDocument}"/> must provide (ADR-004). A provider is not
/// supported until a subclass of this suite passes against it. Durability across process restarts is
/// tested separately per provider.
/// </summary>
public abstract class LocalStoreConformanceTests
{
    /// <summary>Creates an empty store.</summary>
    protected abstract ILocalStore<Note> CreateStore();

    private static SyncRecord<Note> Dirty(string id, long wall, string title = "t") =>
        new(new Note { Id = id, Title = title, UpdatedAt = new HlcTimestamp(wall, 0, "n") }, null, IsDirty: true) { LocalRevision = 1 };

    private static RecordUpdate<Note> Put(SyncRecord<Note> record) => new(record.Current.Id, _ => record);

    [Fact(DisplayName = "Store: an absent record reads as null and the checkpoint starts at Start")]
    public async Task EmptyStore()
    {
        var store = CreateStore();
        Assert.Null(await store.GetAsync("missing"));
        Assert.True((await store.GetCheckpointAsync()).IsStart);
        Assert.Equal(HlcTimestamp.MinValue, await store.GetClockHighWaterAsync());
        Assert.Equal(0, await store.CountDirtyAsync());
        Assert.Empty(await store.GetPendingAsync(10));
    }

    [Fact(DisplayName = "Store I02: every SyncRecord field round-trips")]
    public async Task AllFieldsRoundTrip()
    {
        var store = CreateStore();
        var record = new SyncRecord<Note>(
            new Note { Id = "n1", Title = "current", UpdatedAt = new HlcTimestamp(5, 1, "a") },
            new Note { Id = "n1", Title = "base", UpdatedAt = new HlcTimestamp(3, 0, "b") },
            IsDirty: true)
        {
            BaseVersion = 9_007_199_254_740_993,
            LocalRevision = 7,
            Pending = new PendingOperation<Note>("op-1", 6, 9_007_199_254_740_993, new Note { Id = "n1", Title = "sent", UpdatedAt = new HlcTimestamp(4, 0, "a") }),
            Rejection = new SyncRejection(7, "forbidden", "no"),
            Observed = new Note { Id = "n1", Title = "observed", UpdatedAt = new HlcTimestamp(6, 0, "c") },
            ObservedVersion = 9_007_199_254_740_999,
        };

        await store.UpdateAsync([Put(record)]);
        var read = (await store.GetAsync("n1"))!;

        Assert.Equal("current", read.Current.Title);
        Assert.Equal("base", read.Base!.Title);
        Assert.True(read.IsDirty);
        Assert.Equal(record.BaseVersion, read.BaseVersion);
        Assert.Equal(7, read.LocalRevision);
        Assert.Equal(record.Pending with { Payload = read.Pending!.Payload }, read.Pending);
        Assert.Equal("sent", read.Pending.Payload.Title);
        Assert.Equal(record.Rejection, read.Rejection);
        Assert.Equal("observed", read.Observed!.Title);
        Assert.Equal(record.ObservedVersion, read.ObservedVersion);
        Assert.Equal(new HlcTimestamp(5, 1, "a"), read.Current.UpdatedAt);
    }

    [Fact(DisplayName = "Store I03: a batch and its checkpoint commit together; a throwing transform commits nothing")]
    public async Task BatchIsAtomic()
    {
        var store = CreateStore();
        await store.UpdateAsync([Put(Dirty("a", 1))], new Checkpoint("cp-1"));

        await Assert.ThrowsAsync<InjectedFaultException>(() => store.UpdateAsync(
            [
                new("a", r => r! with { IsDirty = false }),
                Put(Dirty("b", 2)),
                new("c", _ => throw new InjectedFaultException("boom")),
            ],
            new Checkpoint("cp-2")));

        Assert.True((await store.GetAsync("a"))!.IsDirty);
        Assert.Null(await store.GetAsync("b"));
        Assert.Equal(new Checkpoint("cp-1"), await store.GetCheckpointAsync());
    }

    [Fact(DisplayName = "Store I03: a null checkpoint leaves the stored checkpoint unchanged")]
    public async Task NullCheckpointUnchanged()
    {
        var store = CreateStore();
        await store.UpdateAsync([], new Checkpoint("cp-1"));
        await store.UpdateAsync([Put(Dirty("a", 1))]);
        Assert.Equal(new Checkpoint("cp-1"), await store.GetCheckpointAsync());
    }

    [Fact(DisplayName = "Store: a transform returning null reports no change and writes nothing")]
    public async Task NullTransformIsNoChange()
    {
        var store = CreateStore();
        await store.UpdateAsync([Put(Dirty("a", 1, "original"))]);

        var results = await store.UpdateAsync(
        [
            new("a", r =>
            {
                r!.Current.Title = "mutated inside a no-op transform";
                return null;
            }),
            new("absent", _ => null),
        ]);

        Assert.False(results[0].Changed);
        Assert.Equal("original", results[0].Record!.Current.Title);
        Assert.False(results[1].Changed);
        Assert.Null(results[1].Record);
        Assert.Equal("original", (await store.GetAsync("a"))!.Current.Title);
        Assert.Null(await store.GetAsync("absent"));
    }

    [Fact(DisplayName = "Store: duplicate ids in one batch and mismatched result ids are refused without writing")]
    public async Task InvalidBatchesRefused()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateAsync([Put(Dirty("a", 1)), Put(Dirty("a", 2))]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync([Put(Dirty("b", 1)), new("c", _ => Dirty("other", 1))]));
        Assert.Null(await store.GetAsync("a"));
        Assert.Null(await store.GetAsync("b"));
    }

    [Fact(DisplayName = "Store I15: a cancelled update commits nothing")]
    public async Task CancelledUpdateCommitsNothing()
    {
        var store = CreateStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateAsync([Put(Dirty("a", 1))], new Checkpoint("x"), cts.Token));
        Assert.Null(await store.GetAsync("a"));
        Assert.True((await store.GetCheckpointAsync()).IsStart);
    }

    [Fact(DisplayName = "Store I02: stored state never aliases caller objects")]
    public async Task NoAliasing()
    {
        var store = CreateStore();
        var record = Dirty("a", 1, "stored") with
        {
            Pending = new PendingOperation<Note>("op", 1, null, new Note { Id = "a", Title = "payload" }),
            Observed = new Note { Id = "a", Title = "observed" },
        };
        var results = await store.UpdateAsync([Put(record)]);

        record.Current.Title = "changed input";
        record.Pending!.Payload.Title = "changed input";
        record.Observed!.Title = "changed input";
        results[0].Record!.Current.Title = "changed result";
        var read = (await store.GetAsync("a"))!;
        read.Current.Title = "changed read";
        (await store.QueryAsync())[0].Title = "changed query";
        (await store.GetPendingAsync(1))[0].Current.Title = "changed pending";

        var final = (await store.GetAsync("a"))!;
        Assert.Equal("stored", final.Current.Title);
        Assert.Equal("payload", final.Pending!.Payload.Title);
        Assert.Equal("observed", final.Observed!.Title);
    }

    [Fact(DisplayName = "Store I19: pending records are ordered by origin time then id, and honour limit and exclusions")]
    public async Task PendingOrderingAndExclusion()
    {
        var store = CreateStore();
        await store.UpdateAsync(
        [
            Put(Dirty("b", 2)),
            Put(Dirty("a", 2)),
            Put(Dirty("z", 1)),
            Put(Dirty("clean", 0) with { IsDirty = false }),
            Put(Dirty("rejected", 0) with { Rejection = new SyncRejection(1, "forbidden", null) }),
            Put(Dirty("é", 2)),
        ]);

        Assert.Equal(["z", "a", "b", "é"], (await store.GetPendingAsync(10)).Select(r => r.Current.Id));
        Assert.Equal(["z", "a"], (await store.GetPendingAsync(2)).Select(r => r.Current.Id));
        Assert.Equal(["b", "é"], (await store.GetPendingAsync(10, new HashSet<string>(StringComparer.Ordinal) { "z", "a" })).Select(r => r.Current.Id));
        Assert.Equal(5, await store.CountDirtyAsync()); // rejected records are dirty but not pushable
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.GetPendingAsync(0));
    }

    [Fact(DisplayName = "Store: queries hide tombstones unless asked")]
    public async Task QueryHidesTombstones()
    {
        var store = CreateStore();
        var tombstone = Dirty("gone", 1);
        tombstone.Current.Deleted = true;
        await store.UpdateAsync([Put(Dirty("live", 1)), Put(tombstone)]);

        Assert.Equal(["live"], (await store.QueryAsync()).Select(n => n.Id));
        Assert.Equal(2, (await store.QueryAsync(includeDeleted: true)).Count);
    }

    [Fact(DisplayName = "Store I12: the clock high-water mark covers committed current and pending timestamps and never decreases")]
    public async Task ClockHighWater()
    {
        var store = CreateStore();
        await store.UpdateAsync([Put(Dirty("a", 5))]);
        await store.UpdateAsync([Put(Dirty("b", 3) with { Pending = new PendingOperation<Note>("op", 1, null, new Note { Id = "b", UpdatedAt = new HlcTimestamp(9, 0, "n") }) })]);
        Assert.Equal(new HlcTimestamp(9, 0, "n"), await store.GetClockHighWaterAsync());

        await store.UpdateAsync([Put(Dirty("b", 1))]);
        await Assert.ThrowsAsync<InjectedFaultException>(() => store.UpdateAsync([Put(Dirty("c", 50)), new("d", _ => throw new InjectedFaultException("x"))]));
        Assert.Equal(new HlcTimestamp(9, 0, "n"), await store.GetClockHighWaterAsync());
    }

    [Fact(DisplayName = "Store T59: non-ASCII and maximum-length ids are stored and compared ordinally")]
    public async Task UnusualIds()
    {
        var store = CreateStore();
        var ids = new[] { "ノート", "😀", "A", "a", new string('x', SyncIds.MaxLength) };
        await store.UpdateAsync(ids.Select(id => Put(Dirty(id, 1))).ToList());

        foreach (var id in ids)
        {
            Assert.Equal(id, (await store.GetAsync(id))!.Current.Id);
        }

        Assert.Equal(ids.Order(StringComparer.Ordinal), (await store.GetPendingAsync(10)).Select(r => r.Current.Id));
    }

    [Fact(DisplayName = "Store I02: concurrent transforms on one record are serialized (no lost increments)")]
    public async Task ConcurrentTransformsSerialize()
    {
        var store = CreateStore();
        await store.UpdateAsync([Put(Dirty("counter", 1) with { LocalRevision = 0 })]);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            store.UpdateAsync([new("counter", r => r! with { LocalRevision = r.LocalRevision + 1 })]))));

        Assert.Equal(50, (await store.GetAsync("counter"))!.LocalRevision);
    }
}

public sealed class InMemoryLocalStoreConformanceTests : LocalStoreConformanceTests
{
    protected override ILocalStore<Note> CreateStore() => new InMemoryLocalStore<Note>(NoteJson.Clone);
}
