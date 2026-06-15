namespace BlazorSync.Protocol;

/// <summary>
/// A request to pull the next batch of server changes for a collection, starting strictly after
/// <see cref="Since"/>.
/// </summary>
/// <param name="Since">The client's current checkpoint. <see cref="Checkpoint.Start"/> for a full sync.</param>
/// <param name="BatchSize">The maximum number of documents the server should return.</param>
public readonly record struct PullRequest(Checkpoint Since, int BatchSize);

/// <summary>
/// The result of a pull: a batch of documents (newest writes after the requested checkpoint) and
/// the checkpoint that should be sent on the next pull.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Documents">
/// Documents written after the requested checkpoint, in ascending (UpdatedAt, Id) order. Includes
/// soft-deleted records so deletions propagate.
/// </param>
/// <param name="Checkpoint">The checkpoint to resume from on the next pull.</param>
/// <param name="HasMore">
/// <see langword="true"/> when the server may have more changes immediately available (the batch
/// was filled). When <see langword="false"/> the client switches from catch-up to live observation.
/// </param>
public sealed record PullResult<TDocument>(
    IReadOnlyList<TDocument> Documents,
    Checkpoint Checkpoint,
    bool HasMore)
    where TDocument : class, ISyncEntity;
