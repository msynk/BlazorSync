using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Tests.TestSupport;
using Xunit;

namespace BlazorSync.Tests;

public sealed class ConflictHandlerTests
{
    private static ConflictContext<Note> Context(string forkBody, string masterBody, long forkTime, long masterTime)
    {
        var fork = new Note { Id = "n1", Body = forkBody, UpdatedAt = new HlcTimestamp(forkTime, 0, "client") };
        var master = new Note { Id = "n1", Body = masterBody, UpdatedAt = new HlcTimestamp(masterTime, 0, "server") };
        var assumed = new Note { Id = "n1", Body = "original", UpdatedAt = new HlcTimestamp(1, 0, "server") };
        return new ConflictContext<Note>(master, assumed, fork);
    }

    [Fact]
    public void ClientWins_KeepsTheFork()
    {
        var handler = new ClientWinsConflictHandler<Note>();

        var resolution = handler.Resolve(Context("local edit", "remote edit", 10, 20));

        Assert.Equal(ConflictOutcome.UseResolved, resolution.Outcome);
        Assert.Equal("local edit", resolution.Resolved!.Body);
    }

    [Fact]
    public void ServerWins_DiscardsTheFork()
    {
        var handler = new ServerWinsConflictHandler<Note>();

        var resolution = handler.Resolve(Context("local edit", "remote edit", 10, 20));

        Assert.Equal(ConflictOutcome.UseMaster, resolution.Outcome);
        Assert.Null(resolution.Resolved);
    }

    [Fact]
    public void LastWriteWins_PrefersTheNewerTimestamp()
    {
        var handler = new LastWriteWinsConflictHandler<Note>();

        var forkNewer = handler.Resolve(Context("local", "remote", forkTime: 30, masterTime: 20));
        var masterNewer = handler.Resolve(Context("local", "remote", forkTime: 10, masterTime: 20));

        // The winning fork is kept with its original authoring timestamp rather than re-stamped.
        Assert.Equal(ConflictOutcome.KeepFork, forkNewer.Outcome);
        Assert.Equal(ConflictOutcome.UseMaster, masterNewer.Outcome);
    }

    [Fact]
    public void DelegateHandler_CanMergeFields()
    {
        var handler = new DelegateConflictHandler<Note>(ctx =>
        {
            var merged = ctx.Fork;
            merged.Body = $"{ctx.RealMaster.Body}|{ctx.Fork.Body}";
            return ConflictResolution<Note>.Resolve(merged);
        });

        var resolution = handler.Resolve(Context("mine", "theirs", 10, 20));

        Assert.Equal("theirs|mine", resolution.Resolved!.Body);
    }
}
