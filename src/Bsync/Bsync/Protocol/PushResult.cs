using System.Text.Json.Serialization;

namespace Bsync.Protocol;

/// <summary>
/// The result of a push: one outcome per operation. An operation without an outcome has an unknown
/// result and must be retried with the same id.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Outcomes">The per-operation outcomes.</param>
public sealed record PushResult<TDocument>(
    [property: JsonPropertyName("outcomes"), JsonRequired] IReadOnlyList<PushOutcome<TDocument>> Outcomes)
    where TDocument : class, ISyncEntity;
