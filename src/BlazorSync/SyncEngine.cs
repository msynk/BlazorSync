using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Documents;
using BlazorSync.Protocol;
using BlazorSync.Storage;
using BlazorSync.Transport;

namespace BlazorSync;

/// <summary>
/// Orchestrates replication of a single collection between a local store and a server transport.
/// <para>
/// All protocol logic lives here so platform stores and the backend stay simple. The engine
/// implements the two RxDB-style phases — <see cref="PullAsync"/> (checkpoint iteration, to catch up
/// the local store) and <see cref="PushAsync"/> (send local writes and resolve conflicts on the
/// client) — and the local write helpers (<see cref="WriteAsync"/>, <see cref="DeleteAsync"/>) that
/// stamp Hybrid Logical Clock timestamps and queue changes for push.
/// </para>
/// </summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class SyncEngine<TDocument>
    where TDocument : class, ISyncEntity
{
    private readonly ILocalStore<TDocument> _store;
    private readonly ISyncTransport<TDocument> _transport;
    private readonly IConflictHandler<TDocument> _conflictHandler;
    private readonly HybridLogicalClock _clock;
    private readonly SyncOptions<TDocument> _options;
    private readonly Func<TDocument, TDocument> _clone;

    /// <summary>Creates an engine for one collection.</summary>
    /// <param name="store">The local persistence layer.</param>
    /// <param name="transport">The client-side view of the server.</param>
    /// <param name="clock">The Hybrid Logical Clock used to stamp local writes.</param>
    /// <param name="conflictHandler">
    /// The conflict strategy. Defaults to <see cref="ClientWinsConflictHandler{TDocument}"/>.
    /// </param>
    /// <param name="options">Optional tuning; sensible defaults are used when omitted.</param>
#pragma warning disable IL2026, IL3050 // Default JSON cloner is reflection-based; callers targeting AOT/trimming supply options.Cloner.
    public SyncEngine(
        ILocalStore<TDocument> store,
        ISyncTransport<TDocument> transport,
        HybridLogicalClock clock,
        IConflictHandler<TDocument>? conflictHandler = null,
        SyncOptions<TDocument>? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _transport = transport;
        _clock = clock;
        _conflictHandler = conflictHandler ?? new ClientWinsConflictHandler<TDocument>();
        _options = options ?? new SyncOptions<TDocument>();
        _clone = _options.Cloner ?? (static doc => DocumentCloner.JsonClone(doc));
    }
#pragma warning restore IL2026, IL3050

    /// <summary>Returns the app-visible documents in the local store.</summary>
    public Task<IReadOnlyList<TDocument>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        _store.QueryAsync(includeDeleted, cancellationToken);

    /// <summary>Returns the stored record (including sync metadata) for <paramref name="id"/>.</summary>
    public Task<SyncRecord<TDocument>?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _store.GetAsync(id, cancellationToken);

    /// <summary>
    /// Applies a local create or update. The document is stamped with a fresh HLC timestamp and
    /// queued for push. The existing server baseline (if any) is preserved so conflicts can still be
    /// detected against the last known server state.
    /// </summary>
    public async Task WriteAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var existing = await _store.GetAsync(document.Id, cancellationToken).ConfigureAwait(false);
        document.UpdatedAt = _clock.Now();
        var baseline = existing?.Base is { } b ? _clone(b) : null;

        await _store.UpsertAsync(
            new SyncRecord<TDocument>(_clone(document), baseline, IsDirty: true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Soft-deletes the document with <paramref name="id"/> (sets <see cref="ISyncEntity.Deleted"/>
    /// and queues it for push). No-op if the document is unknown locally.
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        var deleted = _clone(existing.Current);
        deleted.Deleted = true;
        deleted.UpdatedAt = _clock.Now();
        var baseline = existing.Base is { } b ? _clone(b) : null;

        await _store.UpsertAsync(
            new SyncRecord<TDocument>(deleted, baseline, IsDirty: true),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a full sync: pull server changes, then push local writes. Returns aggregate stats.</summary>
    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var pull = await PullAsync(cancellationToken).ConfigureAwait(false);
        var push = await PushAsync(cancellationToken).ConfigureAwait(false);
        return pull + push;
    }

    /// <summary>
    /// Checkpoint-iteration pull: repeatedly fetches batches after the stored checkpoint and applies
    /// them, until the server reports no more changes. Dirty (locally-modified) records are left
    /// untouched so their divergence is detected during the next push.
    /// </summary>
    public async Task<SyncResult> PullAsync(CancellationToken cancellationToken = default)
    {
        var applied = 0;
        var checkpoint = await _store.GetCheckpointAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _transport
                .PullAsync(new PullRequest(checkpoint, _options.PullBatchSize), cancellationToken)
                .ConfigureAwait(false);

            foreach (var serverDoc in result.Documents)
            {
                // Keep the local clock ahead of anything observed from the server.
                _clock.Update(serverDoc.UpdatedAt);

                var existing = await _store.GetAsync(serverDoc.Id, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    var clone = _clone(serverDoc);
                    await _store.UpsertAsync(
                        new SyncRecord<TDocument>(clone, _clone(serverDoc), IsDirty: false),
                        cancellationToken).ConfigureAwait(false);
                    applied++;
                }
                else if (!existing.IsDirty)
                {
                    // Fast-forward: no local changes, so adopt the server state outright.
                    await _store.UpsertAsync(
                        new SyncRecord<TDocument>(_clone(serverDoc), _clone(serverDoc), IsDirty: false),
                        cancellationToken).ConfigureAwait(false);
                    applied++;
                }
                // else: dirty local record — preserve divergence; the conflict surfaces during push.
            }

            checkpoint = result.Checkpoint;
            await _store.SetCheckpointAsync(checkpoint, cancellationToken).ConfigureAwait(false);

            if (!result.HasMore || result.Documents.Count == 0)
            {
                break;
            }
        }

        return new SyncResult(applied, 0, 0);
    }

    /// <summary>
    /// Pushes dirty records to the server. Accepted writes adopt the server-authoritative state;
    /// conflicts are resolved by the configured <see cref="IConflictHandler{TDocument}"/>. Because a
    /// resolution can produce a merged document that must itself be pushed, this runs additional
    /// passes until the push queue drains or <see cref="SyncOptions{TDocument}.MaxPushPasses"/> is hit.
    /// </summary>
    public async Task<SyncResult> PushAsync(CancellationToken cancellationToken = default)
    {
        var pushed = 0;
        var conflicts = 0;

        for (var pass = 0; pass < _options.MaxPushPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirty = await _store.GetDirtyAsync(_options.PushBatchSize, cancellationToken).ConfigureAwait(false);
            if (dirty.Count == 0)
            {
                break;
            }

            var rows = new List<PushRow<TDocument>>(dirty.Count);
            var pushedState = new Dictionary<string, TDocument>(dirty.Count, StringComparer.Ordinal);
            foreach (var record in dirty)
            {
                rows.Add(new PushRow<TDocument>(record.Base, record.Current));
                pushedState[record.Current.Id] = record.Current;
            }

            var result = await _transport
                .PushAsync(new PushRequest<TDocument>(rows), cancellationToken)
                .ConfigureAwait(false);

            foreach (var accepted in result.Accepted)
            {
                await ApplyAcceptedAsync(accepted, pushedState, cancellationToken).ConfigureAwait(false);
                pushed++;
            }

            foreach (var realMaster in result.Conflicts)
            {
                await ResolveConflictAsync(realMaster, cancellationToken).ConfigureAwait(false);
                conflicts++;
            }

            // If nothing conflicted, a single pass drained everything pushable.
            if (result.Conflicts.Count == 0)
            {
                break;
            }
        }

        return new SyncResult(0, pushed, conflicts);
    }

    private async Task ApplyAcceptedAsync(
        TDocument accepted,
        IReadOnlyDictionary<string, TDocument> pushedState,
        CancellationToken cancellationToken)
    {
        _clock.Update(accepted.UpdatedAt);
        var existing = await _store.GetAsync(accepted.Id, cancellationToken).ConfigureAwait(false);

        // If the local current state still matches what we pushed, the record is fully reconciled.
        // If the user edited it again mid-sync, keep it dirty but rebase its baseline on the server state.
        var editedSincePush =
            existing is not null
            && pushedState.TryGetValue(accepted.Id, out var sent)
            && existing.Current.UpdatedAt != sent.UpdatedAt;

        if (editedSincePush)
        {
            await _store.UpsertAsync(
                new SyncRecord<TDocument>(existing!.Current, _clone(accepted), IsDirty: true),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _store.UpsertAsync(
                new SyncRecord<TDocument>(_clone(accepted), _clone(accepted), IsDirty: false),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ResolveConflictAsync(TDocument realMaster, CancellationToken cancellationToken)
    {
        _clock.Update(realMaster.UpdatedAt);
        var existing = await _store.GetAsync(realMaster.Id, cancellationToken).ConfigureAwait(false);

        // Fork should exist (we pushed it); if it vanished, just adopt the server state.
        if (existing is null)
        {
            await _store.UpsertAsync(
                new SyncRecord<TDocument>(_clone(realMaster), _clone(realMaster), IsDirty: false),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var context = new ConflictContext<TDocument>(
            RealMaster: _clone(realMaster),
            AssumedMaster: existing.Base is { } b ? _clone(b) : null,
            Fork: _clone(existing.Current));

        var resolution = _conflictHandler.Resolve(context);

        if (resolution.Outcome == ConflictOutcome.UseMaster || resolution.Resolved is null)
        {
            await _store.UpsertAsync(
                new SyncRecord<TDocument>(_clone(realMaster), _clone(realMaster), IsDirty: false),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Re-push the resolved document. Stamp it so it causally dominates the server state, and
        // rebase its assumed-master onto the real master so the re-push is accepted.
        var resolved = _clone(resolution.Resolved);
        resolved.Id = realMaster.Id;
        resolved.UpdatedAt = _clock.Update(realMaster.UpdatedAt);

        await _store.UpsertAsync(
            new SyncRecord<TDocument>(resolved, _clone(realMaster), IsDirty: true),
            cancellationToken).ConfigureAwait(false);
    }
}
