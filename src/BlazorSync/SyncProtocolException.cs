namespace BlazorSync;

/// <summary>
/// Thrown when a peer violates the replication protocol (for example a push response that reports an
/// unknown or duplicated operation, or a pull page that does not advance). The engine raises it before
/// applying any part of the offending message, so local state is left unchanged.
/// </summary>
public class SyncProtocolException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SyncProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public SyncProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when the server can no longer serve the client's checkpoint (for example the checkpoint
/// belongs to a different server epoch). Recovery requires a snapshot/reset flow that preserves
/// pending local work; that flow is not implemented yet (see <c>docs/roadmap.md</c>).
/// </summary>
public sealed class SyncResetRequiredException : SyncProtocolException
{
    /// <summary>Creates the exception.</summary>
    public SyncResetRequiredException(string message)
        : base(message)
    {
    }
}
