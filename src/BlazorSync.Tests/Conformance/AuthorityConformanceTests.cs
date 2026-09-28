using BlazorSync.Clocks;
using BlazorSync.Protocol;
using BlazorSync.Server;
using BlazorSync.Tests.TestSupport;
using BlazorSync.Transport;
using Xunit;

namespace BlazorSync.Tests.Conformance;

/// <summary>
/// The push/pull semantics every authority must provide, observed only through
/// <see cref="ISyncTransport{TDocument}"/> (ADR-002, ADR-005). A server implementation is not supported
/// until a subclass passes. Multi-instance, restart and commit-ordering tests are added per provider.
/// </summary>
public abstract class AuthorityConformanceTests
{
    /// <summary>Creates an empty authority whose clock-skew check uses <paramref name="clock"/>.</summary>
    protected abstract ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null);

    private readonly ManualClock _clock = new(1_000_000);

    private ISyncTransport<Note> Create(Func<PushOperation<Note>, Note?, string?>? validator = null) => CreateAuthority(_clock, validator);

    private static PushOperation<Note> Op(string opId, string docId, long? baseVersion, string title = "t", long wall = 1_000_000) =>
        new(opId, docId, baseVersion, new Note { Id = docId, Title = title, UpdatedAt = new HlcTimestamp(wall, 0, "n") });

    private static async Task<IReadOnlyList<PushOutcome<Note>>> Push(ISyncTransport<Note> authority, params PushOperation<Note>[] ops) =>
        (await authority.PushAsync(new PushRequest<Note>(ops))).Outcomes;

    private static async Task<List<RemoteChange<Note>>> PullAll(ISyncTransport<Note> authority, int batch = 2)
    {
        var all = new List<RemoteChange<Note>>();
        var checkpoint = Checkpoint.Start;
        while (true)
        {
            var page = await authority.PullAsync(new PullRequest(checkpoint, batch));
            Assert.InRange(page.Changes.Count, 0, batch);
            Assert.Equal(page.Changes.Count, page.Changes.Select(c => c.Document.Id).Distinct(StringComparer.Ordinal).Count());
            all.AddRange(page.Changes);
            if (page.HasMore)
            {
                Assert.NotEqual(checkpoint, page.Checkpoint);
            }

            checkpoint = page.Checkpoint;
            if (!page.HasMore)
            {
                return all;
            }
        }
    }

    [Fact(DisplayName = "Authority: exactly one outcome per operation, in request order")]
    public async Task OneOutcomePerOperation()
    {
        var authority = Create();
        var outcomes = await Push(authority, Op("o1", "a", null), Op("o2", "b", null), Op("o3", "c", null));
        Assert.Equal(["o1", "o2", "o3"], outcomes.Select(o => o.OperationId));
        Assert.All(outcomes, o => Assert.Equal(PushOutcomeKind.Accepted, o.Kind));
    }

    [Fact(DisplayName = "Authority I05: accepted versions strictly increase per document; a stale base conflicts")]
    public async Task VersionsAndConflicts()
    {
        var authority = Create();
        var v1 = (await Push(authority, Op("o1", "a", null, "v1")))[0].Version!.Value;
        var v2 = (await Push(authority, Op("o2", "a", v1, "v2")))[0].Version!.Value;
        var stale = (await Push(authority, Op("o3", "a", v1, "stale")))[0];

        Assert.True(v2 > v1);
        Assert.Equal(PushOutcomeKind.Conflict, stale.Kind);
        Assert.Equal(v2, stale.Version);
        Assert.Equal("v2", stale.Document!.Title);
    }

    [Fact(DisplayName = "Authority I05: two writers on the same base cannot both succeed; a same-id insert conflicts")]
    public async Task SameBaseAndSameIdInsert()
    {
        var authority = Create();
        var v1 = (await Push(authority, Op("o0", "a", null)))[0].Version;
        var first = (await Push(authority, Op("oa", "a", v1, "A")))[0];
        var second = (await Push(authority, Op("ob", "a", v1, "B")))[0];
        var insert = (await Push(authority, Op("oc", "a", null, "C")))[0];

        Assert.Equal(PushOutcomeKind.Accepted, first.Kind);
        Assert.Equal(PushOutcomeKind.Conflict, second.Kind);
        Assert.Equal(PushOutcomeKind.Conflict, insert.Kind);
        Assert.Equal("A", (await PullAll(authority)).Single().Document.Title);
    }

    [Fact(DisplayName = "Authority I04: a duplicate delivery replays the original outcome without a second effect")]
    public async Task DuplicateReplays()
    {
        var authority = Create();
        var first = (await Push(authority, Op("o1", "a", null, "x")))[0];
        var replay = (await Push(authority, Op("o1", "a", null, "x")))[0];

        Assert.False(first.IsDuplicate);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(first.Kind, replay.Kind);
        Assert.Equal(first.Version, replay.Version);
        Assert.Single(await PullAll(authority));
        Assert.Equal(first.Version, (await PullAll(authority)).Single().Version);
    }

    [Fact(DisplayName = "Authority I04: a duplicate of a conflicted operation replays the conflict")]
    public async Task DuplicateConflictReplays()
    {
        var authority = Create();
        await Push(authority, Op("o0", "a", null));
        var first = (await Push(authority, Op("o1", "a", null, "late insert")))[0];
        var replay = (await Push(authority, Op("o1", "a", null, "late insert")))[0];

        Assert.Equal(PushOutcomeKind.Conflict, first.Kind);
        Assert.Equal(PushOutcomeKind.Conflict, replay.Kind);
        Assert.True(replay.IsDuplicate);
    }

    [Fact(DisplayName = "Authority I04: an operation id reused with a different payload or base is rejected")]
    public async Task OperationIdReuse()
    {
        var authority = Create();
        var v1 = (await Push(authority, Op("o1", "a", null, "x")))[0].Version;

        var differentPayload = (await Push(authority, Op("o1", "a", null, "y")))[0];
        var differentBase = (await Push(authority, Op("o1", "a", v1, "x")))[0];

        Assert.Equal(PushErrorCodes.OperationIdReused, differentPayload.ErrorCode);
        Assert.Equal(PushErrorCodes.OperationIdReused, differentBase.ErrorCode);
        Assert.Equal("x", (await PullAll(authority)).Single().Document.Title);
    }

    [Fact(DisplayName = "Authority: malformed operations and two operations for one document are rejected individually")]
    public async Task MalformedOperations()
    {
        var authority = Create();
        var outcomes = await Push(
            authority,
            Op("o1", "a", null),
            Op("o2", "a", null),
            new PushOperation<Note>("o3", "b", null, new Note { Id = "not-b" }),
            Op("o4", "c", 0),
            Op("o5", "d", null));

        Assert.Equal(
            [PushOutcomeKind.Accepted, PushOutcomeKind.Rejected, PushOutcomeKind.Rejected, PushOutcomeKind.Rejected, PushOutcomeKind.Accepted],
            outcomes.Select(o => o.Kind));
        Assert.All(outcomes.Where(o => o.Kind == PushOutcomeKind.Rejected), o => Assert.Equal(PushErrorCodes.Invalid, o.ErrorCode));
    }

    [Fact(DisplayName = "Authority I12: origin timestamps are stored unchanged; far-future ones are rejected")]
    public async Task OriginTimestamps()
    {
        var authority = Create();
        var stamp = new HlcTimestamp(_clock.NowMilliseconds() - 5, 3, "device");
        var ok = (await authority.PushAsync(new PushRequest<Note>([new("o1", "a", null, new Note { Id = "a", UpdatedAt = stamp })]))).Outcomes[0];
        var future = (await Push(authority, Op("o2", "b", null, wall: _clock.NowMilliseconds() + (long)TimeSpan.FromHours(1).TotalMilliseconds)))[0];

        Assert.Equal(stamp, ok.Document!.UpdatedAt);
        Assert.Equal(stamp, (await PullAll(authority)).Single().Document.UpdatedAt);
        Assert.Equal(PushErrorCodes.ClockSkew, future.ErrorCode);
    }

    [Fact(DisplayName = "Authority I18: application validation rejects without writing")]
    public async Task ValidationRejects()
    {
        var authority = Create((op, _) => op.Document.Title == "bad" ? PushErrorCodes.Forbidden : null);
        var outcome = (await Push(authority, Op("o1", "a", null, "bad")))[0];

        Assert.Equal(PushOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(PushErrorCodes.Forbidden, outcome.ErrorCode);
        Assert.Empty(await PullAll(authority));
    }

    [Fact(DisplayName = "Authority I06 I10: the feed pages every committed document once, latest version, tombstones included")]
    public async Task FeedCoversEverything()
    {
        var authority = Create();
        var versions = new Dictionary<string, long>();
        for (var i = 0; i < 7; i++)
        {
            versions[$"d{i}"] = (await Push(authority, Op($"o{i}", $"d{i}", null)))[0].Version!.Value;
        }

        var tombstone = new Note { Id = "d3", Deleted = true, UpdatedAt = new HlcTimestamp(1_000_000, 1, "n") };
        versions["d3"] = (await authority.PushAsync(new PushRequest<Note>([new("del", "d3", versions["d3"], tombstone)]))).Outcomes[0].Version!.Value;

        var feed = await PullAll(authority, batch: 3);

        Assert.Equal(versions.OrderBy(kv => kv.Key), feed.ToDictionary(c => c.Document.Id, c => c.Version).OrderBy(kv => kv.Key));
        Assert.True(feed.Single(c => c.Document.Id == "d3").Document.Deleted);
    }

    [Fact(DisplayName = "Authority I06: resuming from a checkpoint returns only later changes, and an idle feed is stable")]
    public async Task ResumeFromCheckpoint()
    {
        var authority = Create();
        await Push(authority, Op("o1", "a", null));
        var first = await authority.PullAsync(new PullRequest(Checkpoint.Start, 10));
        var idle = await authority.PullAsync(new PullRequest(first.Checkpoint, 10));

        await Push(authority, Op("o2", "b", null));
        var next = await authority.PullAsync(new PullRequest(first.Checkpoint, 10));

        Assert.Empty(idle.Changes);
        Assert.False(idle.HasMore);
        Assert.Equal(["b"], next.Changes.Select(c => c.Document.Id));
    }

    [Fact(DisplayName = "Authority I14: a write based on a version the authority does not have is accepted when the document does not exist")]
    public async Task BaseVersionForMissingDocument()
    {
        // A replica's pending edit may be based on state a restore lost; it must not be stranded.
        var authority = Create();
        var outcome = (await Push(authority, Op("o1", "restored-away", 41, "pending edit")))[0];

        Assert.Equal(PushOutcomeKind.Accepted, outcome.Kind);
        Assert.True(outcome.Version > 0);
    }

    [Fact(DisplayName = "Authority I14: a checkpoint issued by another authority requires reset")]
    public async Task ForeignCheckpoint()
    {
        var one = Create();
        await Push(one, Op("o1", "a", null));
        var checkpoint = (await one.PullAsync(new PullRequest(Checkpoint.Start, 10))).Checkpoint;

        await Assert.ThrowsAsync<SyncResetRequiredException>(() => Create().PullAsync(new PullRequest(checkpoint, 10)));
    }
}

public sealed class InMemoryAuthorityConformanceTests : AuthorityConformanceTests
{
    protected override ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null) =>
        new InProcessTransport<Note>(new InMemorySyncServer<Note>(NoteJson.ServerOptions(clock, validator)));
}

/// <summary>The same suite with every message crossing the JSON wire encoding.</summary>
public sealed class InMemoryAuthorityOverJsonConformanceTests : AuthorityConformanceTests
{
    protected override ISyncTransport<Note> CreateAuthority(IPhysicalClock clock, Func<PushOperation<Note>, Note?, string?>? validator = null) =>
        new JsonWireTransport<Note>(new InProcessTransport<Note>(new InMemorySyncServer<Note>(NoteJson.ServerOptions(clock, validator))), NoteJsonContext.Default);
}
