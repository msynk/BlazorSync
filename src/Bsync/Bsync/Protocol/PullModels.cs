using System.Text.Json.Serialization;

namespace Bsync.Protocol;

/// <summary>
/// A request for the next page of the server's change feed, starting strictly after
/// <see cref="Since"/>.
/// </summary>
/// <param name="Since">The client's stored checkpoint. <see cref="Checkpoint.Start"/> for a full sync.</param>
/// <param name="BatchSize">The maximum number of changes the server may return (at least 1).</param>
public readonly record struct PullRequest(
    [property: JsonPropertyName("checkpoint"), JsonRequired] Checkpoint Since,
    [property: JsonPropertyName("limit"), JsonRequired] int BatchSize);

/// <summary>A committed server state of one document, as delivered by the change feed.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Document">
/// The document's current state. Tombstones (<see cref="ISyncEntity.Deleted"/>) are included so
/// deletions propagate.
/// </param>
/// <param name="Version">
/// The server's version of the document: the concurrency token a later push must name as its base.
/// Versions of one document strictly increase.
/// </param>
public sealed record RemoteChange<TDocument>(
    [property: JsonPropertyName("document"), JsonRequired] TDocument Document,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(WireInt64JsonConverter))] long Version)
    where TDocument : class, ISyncEntity;

/// <summary>One page of the change feed.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Changes">
/// Changes after the requested checkpoint in feed order. A page contains at most one change per
/// document and at most <see cref="PullRequest.BatchSize"/> changes.
/// </param>
/// <param name="Checkpoint">
/// The checkpoint to store once <paramref name="Changes"/> are durably applied, and to send on the next
/// request.
/// </param>
/// <param name="HasMore">
/// <see langword="true"/> when more changes are immediately available. A page with
/// <paramref name="HasMore"/> set must advance the checkpoint.
/// </param>
public sealed record PullResult<TDocument>(
    [property: JsonPropertyName("changes"), JsonRequired] IReadOnlyList<RemoteChange<TDocument>> Changes,
    [property: JsonPropertyName("checkpoint"), JsonRequired] Checkpoint Checkpoint,
    [property: JsonPropertyName("hasMore"), JsonRequired] bool HasMore)
    where TDocument : class, ISyncEntity
{
    /// <summary>Optional protocol features this server supports (see <see cref="SyncFeatures"/>); absent means none.</summary>
    [JsonPropertyName("features")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Features { get; init; }
}
