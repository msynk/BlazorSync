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
/// Thrown when the server can no longer serve the client's checkpoint. The engine then resnapshots, keeping
/// pending local work (docs/protocol/v1.md §6.1).
/// </summary>
public sealed class SyncResetRequiredException : SyncProtocolException
{
    /// <summary>Creates the exception.</summary>
    public SyncResetRequiredException(string message, string reason = ResetReasons.Epoch)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>Why the checkpoint cannot be served; one of <see cref="ResetReasons"/>.</summary>
    public string Reason { get; }
}

/// <summary>Reasons for <see cref="SyncResetRequiredException"/>.</summary>
public static class ResetReasons
{
    /// <summary>The server's history changed (for example a restore). Records the snapshot lacks are hidden but kept on the device for inspection.</summary>
    public const string Epoch = "epoch";

    /// <summary>What the caller may see changed (permissions, filter). Records the snapshot lacks are removed from the device.</summary>
    public const string ScopeChanged = "scope-changed";

    /// <summary>The checkpoint is older than the server's retention horizon. Records the snapshot lacks are removed from the device.</summary>
    public const string Expired = "expired";
}
