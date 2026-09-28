using System.Text.Json.Serialization;

namespace Bsync.Protocol;

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
