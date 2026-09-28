using Bsync.Protocol;

namespace Bsync.Server;

/// <summary>
/// An authority that announces commits, so connected hosts can refresh promptly. Subscribers must treat
/// announcements as hints and still reconcile from durable state (I13).
/// </summary>
public interface ISyncCommitNotifier
{
    /// <summary>Raised after changes commit. Handlers run synchronously on the committing thread and must be quick.</summary>
    event Action<AuthorityCommit>? Committed;
}
