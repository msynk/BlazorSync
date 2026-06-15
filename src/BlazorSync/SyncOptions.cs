namespace BlazorSync;

/// <summary>Tuning knobs for a <c>SyncEngine</c>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class SyncOptions<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Maximum documents requested per pull batch. Default 100.</summary>
    public int PullBatchSize { get; init; } = 100;

    /// <summary>Maximum local writes sent per push batch. Default 100.</summary>
    public int PushBatchSize { get; init; } = 100;

    /// <summary>
    /// Safety bound on how many push passes a single sync performs. Each pass can generate new dirty
    /// records when conflicts resolve to a merged document that must be re-pushed; this cap prevents
    /// a pathological loop. Default 16.
    /// </summary>
    public int MaxPushPasses { get; init; } = 16;

    /// <summary>
    /// Deep-clone function used to keep current/base states isolated. Defaults to a JSON round-trip;
    /// supply a source-generated cloner for trimmed/AOT (WASM) builds.
    /// </summary>
    public Func<TDocument, TDocument>? Cloner { get; init; }
}
