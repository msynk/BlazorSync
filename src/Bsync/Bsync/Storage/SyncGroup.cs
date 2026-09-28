namespace Bsync.Storage;

/// <summary>A set of documents whose changes must be applied together (all or nothing).</summary>
/// <param name="Id">The group id.</param>
/// <param name="Members">The ids of the documents in the group.</param>
public sealed record SyncGroup(string Id, IReadOnlyList<string> Members);
