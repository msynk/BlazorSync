using Bsync.Demo.Models;

namespace Bsync.Demo.Services;

/// <summary>The conflict strategies a device can use in the demo.</summary>
public enum ConflictStrategy
{
    /// <summary>Local change wins (Bsync default).</summary>
    ClientWins,

    /// <summary>Server change wins; local change discarded.</summary>
    ServerWins,

    /// <summary>Newer Hybrid Logical Clock timestamp wins.</summary>
    LastWriteWins,

    /// <summary>Custom field-level merge (keeps both edits where possible).</summary>
    FieldMerge,
}
