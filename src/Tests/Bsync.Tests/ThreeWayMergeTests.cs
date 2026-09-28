using System.Text.Json;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Tests.TestSupport;
using Xunit;

namespace Bsync.Tests;

/// <summary>ADR-006, I11: field-level three-way merge semantics and the merging conflict handler.</summary>
public sealed class ThreeWayMergeTests
{
    private static Card Base() => new()
    {
        Title = "base",
        Tags = ["a", "b"],
        Address = new Address { Street = "1 Main", City = "Oslo" },
        Due = "2026-10-01",
        UpdatedAt = new HlcTimestamp(1, 0, "b"),
    };

    private static Card Edit(Action<Card> change, long wall)
    {
        var card = Base();
        change(card);
        card.UpdatedAt = new HlcTimestamp(wall, 0, "e");
        return card;
    }

    private static ThreeWayMergeResult<Card> Merge(Card local, Card server) =>
        ThreeWayMerge.Merge(Base(), local, server, CardJsonContext.Default.Card);

    [Fact(DisplayName = "I11: edits to different fields and nested members combine; timestamps never conflict")]
    public void DisjointEditsCombine()
    {
        var result = Merge(
            Edit(c => { c.Title = "local title"; c.Address.Street = "2 Side"; }, 5),
            Edit(c => { c.Tags = ["a", "b", "c"]; c.Address.City = "Bergen"; }, 7));

        Assert.True(result.IsClean);
        Assert.False(result.SameAsServer);
        Assert.Equal("local title", result.Merged.Title);
        Assert.Equal(["a", "b", "c"], result.Merged.Tags);
        Assert.Equal(("2 Side", "Bergen"), (result.Merged.Address.Street, result.Merged.Address.City));
        Assert.Equal(new HlcTimestamp(7, 0, "e"), result.Merged.UpdatedAt);
    }

    [Fact(DisplayName = "I11: the same field changed differently is a conflict that keeps the server value")]
    public void SameFieldConflicts()
    {
        var result = Merge(Edit(c => { c.Title = "mine"; c.Address.City = "Oslo 2"; }, 5), Edit(c => { c.Title = "theirs"; c.Address.City = "Oslo 3"; }, 7));

        Assert.Equal(["/Title", "/Address/City"], result.Conflicts);
        Assert.Equal(("theirs", "Oslo 3"), (result.Merged.Title, result.Merged.Address.City));
    }

    [Fact(DisplayName = "I11: identical changes on both sides agree; changes already on the server add nothing")]
    public void IdenticalChanges()
    {
        var result = Merge(Edit(c => c.Title = "same", 5), Edit(c => { c.Title = "same"; c.Tags = ["z"]; }, 7));

        Assert.True(result.IsClean);
        Assert.True(result.SameAsServer);
    }

    [Fact(DisplayName = "I11: arrays are atomic: different edits of one array conflict")]
    public void ArraysAreAtomic()
    {
        var result = Merge(Edit(c => c.Tags = ["a", "b", "local"], 5), Edit(c => c.Tags = ["server", "a", "b"], 7));

        Assert.Equal(["/Tags"], result.Conflicts);
        Assert.Equal(["server", "a", "b"], result.Merged.Tags);
    }

    [Fact(DisplayName = "I11: removing a member is a change distinct from null; removal against an edit conflicts")]
    public void AbsentIsAChange()
    {
        var removed = Merge(Edit(c => c.Due = null, 5), Edit(c => c.Title = "theirs", 7));
        Assert.True(removed.IsClean);
        Assert.Null(removed.Merged.Due);
        Assert.Equal("theirs", removed.Merged.Title);

        var conflicting = Merge(Edit(c => c.Due = null, 5), Edit(c => c.Due = "2026-12-24", 7));
        Assert.Equal(["/Due"], conflicting.Conflicts);
        Assert.Equal("2026-12-24", conflicting.Merged.Due);
    }

    [Fact(DisplayName = "I11 I17: fields unknown to this version merge like known ones")]
    public void UnknownFieldsMerge()
    {
        static Card WithUnknown(string json) => JsonSerializer.Deserialize("""{"Id":"c1","Title":"base","Address":{},""" + json + "}", CardJsonContext.Default.Card)!;
        var result = ThreeWayMerge.Merge(
            WithUnknown("""  "priority":1,"owner":"ann"  """),
            WithUnknown("""  "priority":2,"owner":"ann"  """),
            WithUnknown("""  "priority":1,"owner":"bob","added":true  """),
            CardJsonContext.Default.Card);

        Assert.True(result.IsClean);
        var unknown = result.Merged.Unknown!;
        Assert.Equal((2, "bob", true), (unknown["priority"].GetInt32(), unknown["owner"].GetString(), unknown["added"].GetBoolean()));
    }

    [Fact(DisplayName = "I10 I11: delete versus update conflicts; delete versus nothing, and delete versus delete, are clean")]
    public void Deletes()
    {
        var deleteVersusUpdate = Merge(Edit(c => c.Deleted = true, 5), Edit(c => c.Title = "theirs", 7));
        Assert.Equal([ThreeWayMerge.DeletionConflict], deleteVersusUpdate.Conflicts);
        Assert.False(deleteVersusUpdate.Merged.Deleted);

        var updateVersusDelete = Merge(Edit(c => c.Title = "mine", 5), Edit(c => c.Deleted = true, 7));
        Assert.Equal([ThreeWayMerge.DeletionConflict], updateVersusDelete.Conflicts);
        Assert.True(updateVersusDelete.Merged.Deleted);

        var deleteVersusMetadataOnly = Merge(Edit(c => c.Deleted = true, 5), Edit(_ => { }, 7));
        Assert.True(deleteVersusMetadataOnly.IsClean);
        Assert.True(deleteVersusMetadataOnly.Merged.Deleted);
        Assert.False(deleteVersusMetadataOnly.SameAsServer);

        var both = Merge(Edit(c => { c.Deleted = true; c.Title = "x"; }, 5), Edit(c => { c.Deleted = true; c.Title = "y"; }, 7));
        Assert.True(both.IsClean);
        Assert.True(both.SameAsServer);
    }

    private static readonly ThreeWayMergeOptions Semantic = new()
    {
        Sets = new HashSet<string>(StringComparer.Ordinal) { "/Tags" },
        Counters = new HashSet<string>(StringComparer.Ordinal) { "/Views" },
    };

    [Fact(DisplayName = "I11: a member declared a set combines additions and removals from both sides")]
    public void SetsMerge()
    {
        var result = ThreeWayMerge.Merge(Base(), Edit(c => c.Tags = ["a", "b", "c"], 5), Edit(c => c.Tags = ["b", "d"], 7), CardJsonContext.Default.Card, Semantic);

        Assert.True(result.IsClean);
        Assert.Equal(["b", "d", "c"], result.Merged.Tags);
        Assert.Equal(["/Tags"], ThreeWayMerge.Merge(Base(), Edit(c => c.Tags = ["a", "b", "c"], 5), Edit(c => c.Tags = ["b", "d"], 7), CardJsonContext.Default.Card).Conflicts);
    }

    [Fact(DisplayName = "I11: a member declared a counter adds both sides' increments")]
    public void CountersMerge()
    {
        var result = ThreeWayMerge.Merge(Edit(c => c.Views = 10, 1), Edit(c => c.Views = 12, 5), Edit(c => c.Views = 15, 7), CardJsonContext.Default.Card, Semantic);

        Assert.True(result.IsClean);
        Assert.Equal(17, result.Merged.Views);
        var handler = new ThreeWayMergeConflictHandler<Card>(CardJsonContext.Default.Card, options: Semantic);
        var resolution = handler.Resolve(new ConflictContext<Card>(Edit(c => c.Views = 15, 7), Edit(c => c.Views = 10, 1), Edit(c => c.Views = 12, 5)));
        Assert.Equal((ConflictOutcome.UseResolved, 17), (resolution.Outcome, resolution.Resolved!.Views));
    }

    [Fact(DisplayName = "I02: merging never modifies its inputs")]
    public void InputsUntouched()
    {
        var local = Edit(c => c.Title = "mine", 5);
        var server = Edit(c => c.Address.City = "Bergen", 7);

        var result = Merge(local, server);

        Assert.Equal(("mine", "Oslo", new HlcTimestamp(5, 0, "e")), (local.Title, local.Address.City, local.UpdatedAt));
        Assert.Equal(("base", "Bergen"), (server.Title, server.Address.City));
        Assert.NotSame(server.Address, result.Merged.Address);
    }

    [Fact(DisplayName = "I11: the merging handler pushes combined edits; a field conflict is kept for the user")]
    public async Task HandlerThroughEngine()
    {
        var server = InMemorySyncServerRef.Create();
        var merge = new ThreeWayMergeConflictHandler<Note>(NoteJsonContext.Default.Note);
        var other = new TestReplica(server, "other");
        var client = new TestReplica(server, "client", merge);
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "title", Body = "body" });
        await other.Engine.SyncAsync();
        await client.Engine.SyncAsync();

        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "their title", Body = "body" });
        await other.Engine.SyncAsync();
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "title", Body = "my body" });
        var merged = await client.Engine.SyncAsync();

        Assert.Equal(1, merged.Conflicts);
        Assert.True(merged.IsComplete);
        Assert.Equal(("their title", "my body"), (server.Get("n1").Title, server.Get("n1").Body));
        Assert.Null((await client.RecordAsync("n1")).Conflict);

        await other.Engine.SyncAsync();
        await other.Engine.WriteAsync(new Note { Id = "n1", Title = "theirs again", Body = "my body" });
        await other.Engine.SyncAsync();
        await client.Engine.WriteAsync(new Note { Id = "n1", Title = "mine", Body = "my body" });
        await client.Engine.SyncAsync();

        Assert.Equal("theirs again", server.Get("n1").Title);
        Assert.Equal("mine", (await client.RecordAsync("n1")).Conflict!.Local.Title);
    }

    [Fact(DisplayName = "I11: without a common ancestor the merging handler defers to its fallback")]
    public void NoAncestorFallsBack()
    {
        var handler = new ThreeWayMergeConflictHandler<Note>(NoteJsonContext.Default.Note, new ServerWinsConflictHandler<Note>());
        var resolution = handler.Resolve(new ConflictContext<Note>(new Note { Id = "n" }, null, new Note { Id = "n", Title = "offline create" }));

        Assert.Equal(ConflictOutcome.UseMaster, resolution.Outcome);
        Assert.Equal(ConflictOutcome.Defer, new ThreeWayMergeConflictHandler<Note>(NoteJsonContext.Default.Note)
            .Resolve(new ConflictContext<Note>(new Note { Id = "n" }, null, new Note { Id = "n" })).Outcome);
    }
}
