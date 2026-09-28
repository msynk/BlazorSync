namespace Bsync.Storage;

/// <summary>A conditional transformation of one record, applied by <see cref="ILocalStore{TDocument}.UpdateAsync"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Id">The record id. The returned record's <c>Current.Id</c> must equal it.</param>
/// <param name="Transform">Maps the committed state to the new state, or to <see langword="null"/> for no change.</param>
public sealed record RecordUpdate<TDocument>(string Id, Func<SyncRecord<TDocument>?, SyncRecord<TDocument>?> Transform)
    where TDocument : class, ISyncEntity;
