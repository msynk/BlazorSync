namespace BlazorSync;

/// <summary>Tuning knobs and work budgets for a <c>SyncEngine</c>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class SyncOptions<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Maximum documents requested per pull page. Default 100.</summary>
    public int PullBatchSize { get; init; } = 100;

    /// <summary>
    /// Maximum pull pages applied by one pull. When reached, the pull stops at a committed checkpoint
    /// and reports <see cref="SyncResult.HasRemainingWork"/>. Default 1000.
    /// </summary>
    public int MaxPullPages { get; init; } = 1000;

    /// <summary>Maximum operations sent per push request. Default 100.</summary>
    public int PushBatchSize { get; init; } = 100;

    /// <summary>
    /// Maximum push requests sent by one push. Normal queue draining uses as many batches as needed up
    /// to this bound; when it is reached the push reports <see cref="SyncResult.HasRemainingWork"/>.
    /// Default 100.
    /// </summary>
    public int MaxPushBatches { get; init; } = 100;

    /// <summary>
    /// Maximum conflicts resolved for one document within one push. A document that keeps conflicting
    /// is left pending and counted in <see cref="SyncResult.Deferred"/> so that it cannot starve
    /// unrelated documents. Default 3.
    /// </summary>
    public int MaxConflictRetries { get; init; } = 3;

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if any value is out of range.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(PullBatchSize, 1, nameof(PullBatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPullPages, 1, nameof(MaxPullPages));
        ArgumentOutOfRangeException.ThrowIfLessThan(PushBatchSize, 1, nameof(PushBatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPushBatches, 1, nameof(MaxPushBatches));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConflictRetries, 1, nameof(MaxConflictRetries));
    }
}
