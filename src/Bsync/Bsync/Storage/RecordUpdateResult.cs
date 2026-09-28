namespace Bsync.Storage;

/// <summary>The outcome of one <see cref="RecordUpdate{TDocument}"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Record">The record's state after the commit (a copy), or <see langword="null"/> if absent.</param>
/// <param name="Changed">Whether the transform produced a new state.</param>
public sealed record RecordUpdateResult<TDocument>(SyncRecord<TDocument>? Record, bool Changed)
    where TDocument : class, ISyncEntity;
