namespace Bsync.Conflicts;

/// <summary>The decision a conflict handler reaches for a single conflicting document.</summary>
public enum ConflictOutcome
{
    /// <summary>Discard the local change and accept the server's current state.</summary>
    UseMaster = 0,

    /// <summary>
    /// Write a new resolved document. It is a new local edit: the engine stamps it with a fresh
    /// timestamp and re-pushes it based on the server's current version.
    /// </summary>
    UseResolved = 1,

    /// <summary>
    /// Keep the local state unchanged, including its original authoring timestamp, and re-push it
    /// based on the server's current version.
    /// </summary>
    KeepFork = 2,

    /// <summary>
    /// Adopt the server's state for now and keep the local change as an unresolved conflict
    /// (<c>SyncRecord.Conflict</c>) for the application or user to resolve later. Nothing is lost and nothing is
    /// pushed until then.
    /// </summary>
    Defer = 3,
}
