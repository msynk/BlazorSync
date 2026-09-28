namespace Bsync;

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
