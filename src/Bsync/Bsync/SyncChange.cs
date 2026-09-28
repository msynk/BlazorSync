using System.Diagnostics.CodeAnalysis;
using Bsync.Clocks;
using Bsync.Conflicts;
using Bsync.Diagnostics;
using Bsync.Documents;
using Bsync.Protocol;
using Bsync.Storage;
using Bsync.Transport;

namespace Bsync;

/// <summary>One committed store transaction that changed records.</summary>
/// <param name="Kind">What caused the change.</param>
/// <param name="Ids">The ids of the records that changed, in commit order.</param>
public sealed record SyncChange(SyncChangeKind Kind, IReadOnlyList<string> Ids);
