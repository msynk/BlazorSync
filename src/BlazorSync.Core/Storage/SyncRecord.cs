namespace BlazorSync.Core.Storage;

/// <summary>
/// A document together with the sync metadata the engine needs to track divergence between the
/// local "fork" and the server "master".
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
/// <param name="Current">The app-visible state (the fork). What queries return and the user edits.</param>
/// <param name="Base">
/// The last server state this record was synced from (the "assumed master"). Pushes send this as the
/// state the client believed was current, enabling the server to detect concurrent changes.
/// <see langword="null"/> for a record created locally that the server has never acknowledged.
/// </param>
/// <param name="IsDirty">
/// <see langword="true"/> when <paramref name="Current"/> contains local changes not yet confirmed by
/// the server. Dirty records are the push queue and are protected from being overwritten by pulls.
/// </param>
public sealed record SyncRecord<TDocument>(TDocument Current, TDocument? Base, bool IsDirty)
    where TDocument : class, ISyncEntity;
