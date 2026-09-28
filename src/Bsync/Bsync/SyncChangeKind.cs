using System.Diagnostics.CodeAnalysis;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Diagnostics;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Transport;

namespace Bsync;

/// <summary>What caused a <see cref="SyncChange"/>.</summary>
public enum SyncChangeKind
{
    /// <summary>A local write or delete through the engine.</summary>
    Local = 0,

    /// <summary>State received from the server by pull, or records marked missing after a reset.</summary>
    Remote = 1,

    /// <summary>Replication metadata: operations prepared, acknowledged, rejected or resolved after a conflict.</summary>
    Sync = 2,
}
