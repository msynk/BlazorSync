namespace Bsync.Storage;

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
