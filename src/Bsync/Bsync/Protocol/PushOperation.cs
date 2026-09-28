using System.Text.Json.Serialization;

namespace Bsync.Protocol;

/// <summary>
/// One immutable write operation sent from a replica to the server. Retrying the same logical write
/// reuses the same <see cref="OperationId"/> and the same payload, which lets the server return the
/// original outcome instead of applying the write twice.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="OperationId">
/// Globally unique id of the logical write. Reusing an id with a different payload is an error.
/// </param>
/// <param name="DocumentId">The id of the document being written; equal to the document's own id.</param>
/// <param name="BaseVersion">
/// The server version the write was based on, or <see langword="null"/> if the replica believes the
/// document is new. The server accepts the write only if this still equals its current version.
/// </param>
/// <param name="Document">The full new state (or tombstone) of the document.</param>
public sealed record PushOperation<TDocument>(
    [property: JsonPropertyName("operationId"), JsonRequired] string OperationId,
    [property: JsonPropertyName("documentId"), JsonRequired] string DocumentId,
    [property: JsonPropertyName("baseVersion"), JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never), JsonConverter(typeof(WireNullableInt64JsonConverter))] long? BaseVersion,
    [property: JsonPropertyName("document"), JsonRequired] TDocument Document)
    where TDocument : class, ISyncEntity
{
    /// <summary>
    /// A dependency group id (protocol §4.1). All operations of a group are in the same request and are applied together
    /// or not at all. Send only to servers that advertise <see cref="SyncFeatures.Groups"/>.
    /// </summary>
    [JsonPropertyName("group")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Group { get; init; }

    /// <summary>How many operations of <see cref="Group"/> the request carries (the server verifies it).</summary>
    [JsonPropertyName("groupSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int GroupSize { get; init; }
}
