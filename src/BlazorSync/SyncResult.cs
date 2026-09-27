namespace BlazorSync;

/// <summary>Summary of one sync, pull or push run.</summary>
/// <remarks>
/// A run that returns normally is not proof that the replica is current: check
/// <see cref="IsComplete"/>. Even a complete run only means that, when it finished, the local queue
/// had been drained and the pull had reached the checkpoint the server reported as current.
/// </remarks>
/// <param name="Pulled">Number of remote changes applied to the local store.</param>
/// <param name="Pushed">Number of local operations the server accepted.</param>
/// <param name="Conflicts">Number of conflicts encountered and handed to the conflict handler.</param>
public readonly record struct SyncResult(int Pulled, int Pushed, int Conflicts)
{
    /// <summary>Number of operations the server permanently rejected (see <c>SyncRecord.Rejection</c>).</summary>
    public int Rejected { get; init; }

    /// <summary>
    /// Number of operations left pending in this run: a retryable server outcome, an outcome missing
    /// from the response, or a document that exhausted its conflict budget.
    /// </summary>
    public int Deferred { get; init; }

    /// <summary>
    /// <see langword="true"/> when work was known to remain when the run finished: pushable local
    /// changes, or pull pages beyond <see cref="SyncOptions{TDocument}.MaxPullPages"/>.
    /// </summary>
    public bool HasRemainingWork { get; init; }

    /// <summary>Whether the run drained everything it could see without deferring or leaving work.</summary>
    public bool IsComplete => !HasRemainingWork && Deferred == 0;

    /// <summary>Adds two results together (used to aggregate pull and push phases).</summary>
    public static SyncResult operator +(SyncResult a, SyncResult b) =>
        new(a.Pulled + b.Pulled, a.Pushed + b.Pushed, a.Conflicts + b.Conflicts)
        {
            Rejected = a.Rejected + b.Rejected,
            Deferred = a.Deferred + b.Deferred,
            HasRemainingWork = a.HasRemainingWork || b.HasRemainingWork,
        };
}
