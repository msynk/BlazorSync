using System.Text.Json.Serialization;

namespace BlazorSync.Protocol;

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
    where TDocument : class, ISyncEntity;

/// <summary>
/// A request carrying a batch of independent operations. The batch is <em>not</em> atomic: each
/// operation has its own outcome and some may be accepted while others conflict.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Operations">The operations, at most one per document.</param>
public sealed record PushRequest<TDocument>(
    [property: JsonPropertyName("operations"), JsonRequired] IReadOnlyList<PushOperation<TDocument>> Operations)
    where TDocument : class, ISyncEntity;

/// <summary>The server's decision for one operation.</summary>
[JsonConverter(typeof(PushOutcomeKindJsonConverter))]
public enum PushOutcomeKind
{
    /// <summary>The write was committed; the outcome carries the authoritative state and version.</summary>
    [JsonStringEnumMemberName("accepted")]
    Accepted = 0,

    /// <summary>
    /// The base version did not match; nothing was written. The outcome carries the server's current
    /// state and version for conflict resolution.
    /// </summary>
    [JsonStringEnumMemberName("conflict")]
    Conflict = 1,

    /// <summary>
    /// The write was permanently refused (validation, authorization, clock skew, operation id reuse).
    /// Resending the same operation will not succeed.
    /// </summary>
    [JsonStringEnumMemberName("rejected")]
    Rejected = 2,

    /// <summary>The server could not decide now; the same operation should be retried later.</summary>
    [JsonStringEnumMemberName("retry-later")]
    RetryLater = 3,
}

/// <summary>The server's outcome for one <see cref="PushOperation{TDocument}"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="OperationId">The id of the operation this outcome answers.</param>
/// <param name="Kind">The decision.</param>
public sealed record PushOutcome<TDocument>(
    [property: JsonPropertyName("operationId"), JsonRequired] string OperationId,
    [property: JsonPropertyName("kind"), JsonRequired] PushOutcomeKind Kind)
    where TDocument : class, ISyncEntity
{
    /// <summary>The server version after acceptance, or the current version on conflict.</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(WireNullableInt64JsonConverter))]
    public long? Version { get; init; }

    /// <summary>The authoritative state after acceptance, or the current state on conflict.</summary>
    [JsonPropertyName("document")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TDocument? Document { get; init; }

    /// <summary>A stable machine-readable reason for <see cref="PushOutcomeKind.Rejected"/> or <see cref="PushOutcomeKind.RetryLater"/>.</summary>
    [JsonPropertyName("errorCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    /// <summary>A human-readable explanation. Not localized; do not show verbatim to end users.</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    /// <summary>
    /// <see langword="true"/> when the server had already decided this operation and is replaying the
    /// stored outcome without applying anything again.
    /// </summary>
    [JsonPropertyName("duplicate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDuplicate { get; init; }

    /// <summary>Creates an accepted outcome.</summary>
    public static PushOutcome<TDocument> Accepted(string operationId, long version, TDocument document) =>
        new(operationId, PushOutcomeKind.Accepted) { Version = version, Document = document };

    /// <summary>Creates a conflict outcome carrying the server's current state.</summary>
    public static PushOutcome<TDocument> Conflict(string operationId, long version, TDocument current) =>
        new(operationId, PushOutcomeKind.Conflict) { Version = version, Document = current };

    /// <summary>Creates a permanent rejection.</summary>
    public static PushOutcome<TDocument> Rejected(string operationId, string errorCode, string? message = null) =>
        new(operationId, PushOutcomeKind.Rejected) { ErrorCode = errorCode, Message = message };

    /// <summary>Creates a retryable outcome.</summary>
    public static PushOutcome<TDocument> RetryLater(string operationId, string errorCode, string? message = null) =>
        new(operationId, PushOutcomeKind.RetryLater) { ErrorCode = errorCode, Message = message };
}

/// <summary>
/// The result of a push: one outcome per operation. An operation without an outcome has an unknown
/// result and must be retried with the same id.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Outcomes">The per-operation outcomes.</param>
public sealed record PushResult<TDocument>(
    [property: JsonPropertyName("outcomes"), JsonRequired] IReadOnlyList<PushOutcome<TDocument>> Outcomes)
    where TDocument : class, ISyncEntity;

/// <summary>Well-known <see cref="PushOutcome{TDocument}.ErrorCode"/> values.</summary>
public static class PushErrorCodes
{
    /// <summary>The operation or document was malformed.</summary>
    public const string Invalid = "invalid";

    /// <summary>The operation id was already used for a different request.</summary>
    public const string OperationIdReused = "operation-id-reused";

    /// <summary>The document's origin timestamp is further in the future than the server allows.</summary>
    public const string ClockSkew = "clock-skew";

    /// <summary>The caller may not perform this write.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>The server is temporarily unable to decide.</summary>
    public const string Unavailable = "unavailable";
}
