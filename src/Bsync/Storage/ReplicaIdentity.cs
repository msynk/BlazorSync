namespace Bsync.Storage;

/// <summary>Identifies a replica's storage and its current incarnation.</summary>
/// <param name="ReplicaId">Generated once when the storage is created.</param>
/// <param name="Incarnation">
/// Changes when the storage may have been copied or restored (for example a device backup). Use it, or a value
/// derived from it, as the HLC node id so a copy never reuses the original's timestamps.
/// </param>
public sealed record ReplicaIdentity(string ReplicaId, string Incarnation);

/// <summary>
/// Thrown when a durable store cannot be used: storage unavailable (private mode, disabled, closed), quota
/// exceeded, a schema upgrade blocked or performed by another tab, or a stale replica session. A write that
/// fails this way was not committed.
/// </summary>
public sealed class LocalStoreUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public LocalStoreUnavailableException(string reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>
    /// One of <c>unavailable</c>, <c>quota</c>, <c>blocked</c>, <c>outdated</c>, <c>closed</c>,
    /// <c>stale-generation</c> or <c>error</c>.
    /// </summary>
    public string Reason { get; }
}
