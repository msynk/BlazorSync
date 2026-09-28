namespace Bsync;

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
