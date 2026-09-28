using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Bsync.Conflicts;

/// <summary>The result of <see cref="ThreeWayMerge.Merge{TDocument}(TDocument, TDocument, TDocument, JsonTypeInfo{TDocument})"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Merged">The merged document. Conflicting members hold the server value.</param>
/// <param name="Conflicts">JSON Pointers of members changed differently on both sides, and <see cref="ThreeWayMerge.DeletionConflict"/> for delete versus update.</param>
/// <param name="SameAsServer">The merge adds nothing to the server state (the local changes are already there).</param>
public sealed record ThreeWayMergeResult<TDocument>(TDocument Merged, IReadOnlyList<string> Conflicts, bool SameAsServer)
    where TDocument : class, ISyncEntity
{
    /// <summary>Whether the merge has no field conflicts.</summary>
    public bool IsClean => Conflicts.Count == 0;
}
