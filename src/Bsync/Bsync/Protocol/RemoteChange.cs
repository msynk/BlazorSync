using System.Text.Json.Serialization;

namespace Bsync.Protocol;

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
