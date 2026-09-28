namespace BlazorSync.Blazor;

/// <summary>
/// A collection backed by a local replica (browser IndexedDB or native SQLite) through a
/// <see cref="SyncSession{TDocument}"/>. Writes are durable locally at once and uploaded in the background.
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class LocalSyncCollection<TDocument> : ISyncCollection<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly SyncSession<TDocument> _session;
    private readonly Func<CancellationToken, Task<string>> _resolveAccount;

    /// <summary>Creates the collection.</summary>
    /// <param name="session">The session that owns the replica.</param>
    /// <param name="resolveAccount">
    /// Returns the signed-in account. When it changes, the next call switches the session to that account's
    /// replica.
    /// </param>
    public LocalSyncCollection(SyncSession<TDocument> session, Func<CancellationToken, Task<string>> resolveAccount)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolveAccount);
        _session = session;
        _resolveAccount = resolveAccount;
        Capabilities = new SyncCapabilities(session.Host, DurableOfflineWrites: true, WritesConfirmedByServer: false, LiveUpdates: session.LiveHints);
    }

    /// <inheritdoc />
    public SyncCapabilities Capabilities { get; }

    /// <inheritdoc />
    public SyncStatus Status => _session.Status;

    /// <inheritdoc />
    public async Task<TDocument?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        var record = await engine.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return record is { MissingAfterReset: false, Current.Deleted: false } ? record.Current : null;
    }

    /// <inheritdoc />
    public async Task<SyncItemStatus?> GetItemStatusAsync(string id, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        return await engine.GetAsync(id, cancellationToken).ConfigureAwait(false) switch
        {
            null => null,
            { MissingAfterReset: true } => new SyncItemStatus(SyncItemState.MissingAfterReset),
            { Conflict: not null } => new SyncItemStatus(SyncItemState.Conflicted),
            { Rejection: { } rejection } => new SyncItemStatus(SyncItemState.Rejected, rejection.ErrorCode),
            { IsDirty: true } => new SyncItemStatus(SyncItemState.Pending),
            _ => new SyncItemStatus(SyncItemState.Synced),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TDocument>> QueryAsync(SyncQuery<TDocument>? query = null, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        return Queries.Apply(await engine.QueryAsync(cancellationToken: cancellationToken).ConfigureAwait(false), query);
    }

    /// <inheritdoc />
    public async Task<SyncWriteResult> SaveAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        var receipt = await engine.WriteAsync(document, cancellationToken).ConfigureAwait(false);
        await _session.NotifyLocalWriteAsync(cancellationToken).ConfigureAwait(false);
        return new SyncWriteResult(receipt.Id, SyncConfirmation.SavedLocally);
    }

    /// <inheritdoc />
    public async Task<SyncWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        var receipt = await engine.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            return new SyncWriteResult(id, SyncConfirmation.NotFound);
        }

        await _session.NotifyLocalWriteAsync(cancellationToken).ConfigureAwait(false);
        return new SyncWriteResult(id, SyncConfirmation.SavedLocally);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncDocumentConflict<TDocument>>> GetConflictsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        var records = await engine.GetConflictsAsync(limit, cancellationToken).ConfigureAwait(false);
        return [.. records.Select(r => new SyncDocumentConflict<TDocument>(r.Current.Id, r.Conflict!.Local, r.Conflict.Server, r.Conflict.Base))];
    }

    /// <inheritdoc />
    public async Task<SyncWriteResult> ResolveConflictAsync(string id, TDocument resolved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        if (await engine.ResolveConflictAsync(id, resolved, cancellationToken).ConfigureAwait(false) is null)
        {
            return new SyncWriteResult(id, SyncConfirmation.NotFound);
        }

        await _session.NotifyLocalWriteAsync(cancellationToken).ConfigureAwait(false);
        return new SyncWriteResult(id, SyncConfirmation.SavedLocally);
    }

    /// <inheritdoc />
    public async Task<bool> DiscardConflictAsync(string id, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        if (!await engine.DiscardConflictAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await _session.NotifyLocalWriteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<SyncWriteResult> RetryAsync(string id, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        if (await engine.RetryRejectedAsync(id, cancellationToken).ConfigureAwait(false) is null)
        {
            return new SyncWriteResult(id, SyncConfirmation.NotFound);
        }

        await _session.NotifyLocalWriteAsync(cancellationToken).ConfigureAwait(false);
        return new SyncWriteResult(id, SyncConfirmation.SavedLocally);
    }

    /// <inheritdoc />
    public async Task<bool> RevertAsync(string id, CancellationToken cancellationToken = default)
    {
        var engine = await EngineAsync(cancellationToken).ConfigureAwait(false);
        if (!await engine.RevertAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await _session.NotifyLocalWriteAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        _session.Changed += onChanged;
        return new Unsubscriber(() => _session.Changed -= onChanged);
    }

    private async Task<SyncEngine<TDocument>> EngineAsync(CancellationToken cancellationToken)
    {
        var account = await _resolveAccount(cancellationToken).ConfigureAwait(false);
        return await _session.GetEngineAsync(account, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Runs an action once on dispose.</summary>
internal sealed class Unsubscriber(Action action) : IDisposable
{
    private Action? _action = action;

    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
