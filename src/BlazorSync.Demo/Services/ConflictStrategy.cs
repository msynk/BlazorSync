using BlazorSync.Conflicts;
using BlazorSync.Demo.Models;

namespace BlazorSync.Demo.Services;

/// <summary>The conflict strategies a device can use in the demo.</summary>
public enum ConflictStrategy
{
    /// <summary>Local change wins (BlazorSync default).</summary>
    ClientWins,

    /// <summary>Server change wins; local change discarded.</summary>
    ServerWins,

    /// <summary>Newer Hybrid Logical Clock timestamp wins.</summary>
    LastWriteWins,

    /// <summary>Custom field-level merge (keeps both edits where possible).</summary>
    FieldMerge,
}

/// <summary>
/// An <see cref="IConflictHandler{TDocument}"/> whose underlying strategy can be swapped at runtime,
/// so the demo UI can flip a device between client-wins, server-wins, last-write-wins and a custom
/// field merge without rebuilding its sync engine.
/// </summary>
public sealed class SwitchableConflictHandler : IConflictHandler<DemoNote>
{
    private readonly Dictionary<ConflictStrategy, IConflictHandler<DemoNote>> _handlers;

    /// <summary>Creates the handler with all strategies registered.</summary>
    public SwitchableConflictHandler(ConflictStrategy initial = ConflictStrategy.ClientWins)
    {
        Strategy = initial;
        _handlers = new Dictionary<ConflictStrategy, IConflictHandler<DemoNote>>
        {
            [ConflictStrategy.ClientWins] = new ClientWinsConflictHandler<DemoNote>(),
            [ConflictStrategy.ServerWins] = new ServerWinsConflictHandler<DemoNote>(),
            [ConflictStrategy.LastWriteWins] = new LastWriteWinsConflictHandler<DemoNote>(),
            [ConflictStrategy.FieldMerge] = new DelegateConflictHandler<DemoNote>(MergeFields),
        };
    }

    /// <summary>The currently active strategy.</summary>
    public ConflictStrategy Strategy { get; set; }

    /// <inheritdoc />
    public ConflictResolution<DemoNote> Resolve(ConflictContext<DemoNote> context) =>
        _handlers[Strategy].Resolve(context);

    /// <summary>
    /// A custom merge: takes the title from whichever side changed it relative to the common
    /// ancestor (assumed master), and concatenates bodies when both sides edited them. Demonstrates
    /// that a handler has full three-way context (real master, assumed master, fork).
    /// </summary>
    private static ConflictResolution<DemoNote> MergeFields(ConflictContext<DemoNote> context)
    {
        var ancestor = context.AssumedMaster;
        var master = context.RealMaster;
        var fork = context.Fork;

        var merged = fork.Clone();

        // Title: prefer the side that diverged from the ancestor; if both diverged, keep the fork's.
        var forkChangedTitle = ancestor is null || ancestor.Title != fork.Title;
        merged.Title = forkChangedTitle ? fork.Title : master.Title;

        // Body: if both sides changed it, keep both.
        var forkChangedBody = ancestor is null || ancestor.Body != fork.Body;
        var masterChangedBody = ancestor is null || ancestor.Body != master.Body;
        merged.Body = forkChangedBody && masterChangedBody && fork.Body != master.Body
            ? $"{master.Body}\n--- merged ---\n{fork.Body}"
            : (forkChangedBody ? fork.Body : master.Body);

        // Deletion is sticky: if either side deleted, the merged record is deleted.
        merged.Deleted = fork.Deleted || master.Deleted;

        return ConflictResolution<DemoNote>.Resolve(merged);
    }
}
