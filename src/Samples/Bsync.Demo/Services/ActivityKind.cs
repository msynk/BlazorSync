namespace Bsync.Demo.Services;

/// <summary>The severity/category of an <see cref="ActivityEntry"/>, used for colour-coding in the UI.</summary>
public enum ActivityKind
{
    /// <summary>A local read/write on a device.</summary>
    Local,

    /// <summary>A network sync operation.</summary>
    Sync,

    /// <summary>A conflict was detected and resolved.</summary>
    Conflict,

    /// <summary>An offline/connectivity event.</summary>
    Network,

    /// <summary>A workspace-level event (reset, device added).</summary>
    System,
}
