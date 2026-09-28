using System.Text.Json.Serialization;

namespace Bsync.Protocol;

/// <summary>
/// A request carrying a batch of independent operations. The batch is <em>not</em> atomic: each
/// operation has its own outcome and some may be accepted while others conflict.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Operations">The operations, at most one per document.</param>
public sealed record PushRequest<TDocument>(
    [property: JsonPropertyName("operations"), JsonRequired] IReadOnlyList<PushOperation<TDocument>> Operations)
    where TDocument : class, ISyncEntity;
