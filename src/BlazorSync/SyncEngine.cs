using System.Diagnostics.CodeAnalysis;
using BlazorSync.Clocks;
using BlazorSync.Conflicts;
using BlazorSync.Documents;
using BlazorSync.Protocol;
using BlazorSync.Storage;
using BlazorSync.Transport;

namespace BlazorSync;

/// <summary>The local commit receipt returned by <see cref="SyncEngine{TDocument}.WriteAsync"/> and <see cref="SyncEngine{TDocument}.DeleteAsync"/>.</summary>
/// <remarks>
/// A receipt means the write is committed to the local store (with the durability of that store) and
/// queued for upload. It says nothing about server acceptance.
/// </remarks>
/// <param name="Id">The document id.</param>
/// <param name="LocalRevision">The local revision created by the write.</param>
/// <param name="UpdatedAt">The origin timestamp stamped on the stored document.</param>
public readonly record struct LocalWriteReceipt(string Id, long LocalRevision, HlcTimestamp UpdatedAt);

/// <summary>
/// Orchestrates replication of a single collection between a local store and a server transport.
/// </summary>
/// <remarks>
/// <para>
/// Local writes (<see cref="WriteAsync"/>, <see cref="DeleteAsync"/>) commit atomically to the store
/// and never wait for the network. Replication (<see cref="PullAsync"/>, <see cref="PushAsync"/>,
/// <see cref="SyncAsync"/>) is single-flight per engine: overlapping calls run one after another.
/// Every state change the engine makes is an atomic compare-and-transform in the store, so a local
/// edit made while a request is in flight is never overwritten or marked clean by that request.
/// </para>
/// <para>
/// Push operations carry a persisted operation id and immutable payload. When a response is lost the
/// same operation is resent, and a conforming server replays its original outcome instead of applying
/// the write again.
/// </para>
/// <para>
/// Only one engine may replicate a given store at a time; the single-flight guarantee does not extend
/// across engine instances or processes.
/// </para>
/// </remarks>
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
    private readonly SemaphoreSlim _replicationGate = new(1, 1);
    private readonly object _initGate = new();
    private Task? _initialization;

    /// <summary>
    /// Creates an engine for one collection that clones documents with reflection-based JSON. Not
    /// trim/AOT safe; use the overload that takes a cloner in trimmed or AOT-compiled apps.
    /// </summary>
    /// <param name="store">The local persistence layer.</param>
    /// <param name="transport">The client-side view of the server.</param>
    /// <param name="clock">
    /// The Hybrid Logical Clock used to stamp local writes. Before its first write the engine advances
    /// it past the store's high-water mark, so timestamps are not reused after a restart.
    /// </param>
    /// <param name="conflictHandler">
    /// The conflict strategy. Defaults to <see cref="ClientWinsConflictHandler{TDocument}"/>.
    /// </param>
    /// <param name="options">Optional tuning; sensible defaults are used when omitted.</param>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    [RequiresUnreferencedCode("Clones documents with reflection-based JSON. Use the constructor that takes a cloner for trimmed or AOT targets.")]
    [RequiresDynamicCode("Clones documents with reflection-based JSON. Use the constructor that takes a cloner for trimmed or AOT targets.")]
    public SyncEngine(
        ILocalStore<TDocument> store,
        ISyncTransport<TDocument> transport,
        HybridLogicalClock clock,
        IConflictHandler<TDocument>? conflictHandler = null,
        SyncOptions<TDocument>? options = null)
        : this(store, transport, clock, static doc => DocumentCloner.JsonClone(doc), conflictHandler, options)
    {
    }

    /// <summary>Creates an engine for one collection with an explicit, trim/AOT-safe cloner.</summary>
    /// <param name="store">The local persistence layer.</param>
    /// <param name="transport">The client-side view of the server.</param>
    /// <param name="clock">The Hybrid Logical Clock used to stamp local writes.</param>
    /// <param name="cloner">
    /// Returns a deep, independent copy of a document, for example <c>doc =&gt; doc.Clone()</c> or
    /// <see cref="DocumentCloner.Json{T}"/> with source-generated metadata.
    /// </param>
    /// <param name="conflictHandler">The conflict strategy. Defaults to client-wins.</param>
    /// <param name="options">Optional tuning; sensible defaults are used when omitted.</param>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    public SyncEngine(
        ILocalStore<TDocument> store,
        ISyncTransport<TDocument> transport,
        HybridLogicalClock clock,
        Func<TDocument, TDocument> cloner,
        IConflictHandler<TDocument>? conflictHandler = null,
        SyncOptions<TDocument>? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(cloner);

        _options = options ?? new SyncOptions<TDocument>();
        _options.Validate();
        _store = store;
        _transport = transport;
        _clock = clock;
        _conflictHandler = conflictHandler ?? new ClientWinsConflictHandler<TDocument>();
        _clone = cloner;
    }

    /// <summary>Returns the app-visible documents in the local store.</summary>
    public Task<IReadOnlyList<TDocument>> QueryAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) =>
        _store.QueryAsync(includeDeleted, cancellationToken);

    /// <summary>Returns the stored record (including sync metadata) for <paramref name="id"/>.</summary>
    public Task<SyncRecord<TDocument>?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _store.GetAsync(id, cancellationToken);

    /// <summary>Returns the number of documents with local changes not yet confirmed by the server.</summary>
    public Task<int> CountDirtyAsync(CancellationToken cancellationToken = default) =>
        _store.CountDirtyAsync(cancellationToken);

    /// <summary>
    /// Commits a local create or update and queues it for push. A copy of <paramref name="document"/>
    /// is stored with a fresh HLC timestamp; the caller's object is not modified. The last-known server
    /// baseline is preserved so conflicts are still detected against it.
    /// </summary>
    /// <exception cref="ArgumentException">The document id is invalid.</exception>
    public async Task<LocalWriteReceipt> WriteAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        SyncIds.Validate(document.Id, nameof(document));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var copy = _clone(document);
        copy.UpdatedAt = _clock.Now();

        var results = await _store.UpdateAsync(
            [new RecordUpdate<TDocument>(copy.Id, existing => existing is null
                ? new SyncRecord<TDocument>(copy, null, IsDirty: true) { LocalRevision = 1 }
                : existing with { Current = copy, IsDirty = true, LocalRevision = existing.LocalRevision + 1, Rejection = null })],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new LocalWriteReceipt(copy.Id, results[0].Record!.LocalRevision, copy.UpdatedAt);
    }

    /// <summary>
    /// Soft-deletes the document with <paramref name="id"/> (stores a tombstone and queues it for push).
    /// Returns <see langword="null"/> without writing if the document is unknown locally.
    /// </summary>
    public async Task<LocalWriteReceipt?> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        SyncIds.Validate(id);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var stamp = _clock.Now();
        var results = await _store.UpdateAsync(
            [new RecordUpdate<TDocument>(id, existing =>
            {
                if (existing is null)
                {
                    return null;
                }

                var tombstone = existing.Current;
                tombstone.Deleted = true;
                tombstone.UpdatedAt = stamp;
                return existing with { Current = tombstone, IsDirty = true, LocalRevision = existing.LocalRevision + 1, Rejection = null };
            })],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return results[0] is { Changed: true, Record: { } record }
            ? new LocalWriteReceipt(id, record.LocalRevision, stamp)
            : null;
    }

    /// <summary>Runs a full sync: pull server changes, then push local writes.</summary>
    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _replicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pull = await PullCoreAsync(cancellationToken).ConfigureAwait(false);
            var push = await PushCoreAsync(cancellationToken).ConfigureAwait(false);
            return pull + push;
        }
        finally
        {
            _replicationGate.Release();
        }
    }

    /// <summary>
    /// Pulls change-feed pages after the stored checkpoint and applies each page, together with its
    /// checkpoint, in one atomic store update. Records with unconfirmed local changes are left untouched
    /// so their divergence is resolved during push.
    /// </summary>
    public async Task<SyncResult> PullAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _replicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PullCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _replicationGate.Release();
        }
    }

    /// <summary>
    /// Pushes pending local changes in batches until the queue is drained or a work budget is reached.
    /// Accepted operations adopt the server's authoritative state unless the record was edited again in
    /// the meantime; conflicts are resolved by the configured <see cref="IConflictHandler{TDocument}"/>.
    /// </summary>
    public async Task<SyncResult> PushAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _replicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PushCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _replicationGate.Release();
        }
    }

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        lock (_initGate)
        {
            // Shared by all callers, so it must not observe any one caller's cancellation.
            if (_initialization is null || _initialization.IsFaulted)
            {
                _initialization = InitializeAsync();
            }

            return _initialization.WaitAsync(cancellationToken);
        }
    }

    private async Task InitializeAsync()
    {
        var highWater = await _store.GetClockHighWaterAsync(CancellationToken.None).ConfigureAwait(false);
        if (highWater != HlcTimestamp.MinValue)
        {
            _clock.Update(highWater);
        }
    }

    private async Task<SyncResult> PullCoreAsync(CancellationToken cancellationToken)
    {
        var applied = 0;
        var checkpoint = await _store.GetCheckpointAsync(cancellationToken).ConfigureAwait(false);

        for (var page = 0; page < _options.MaxPullPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _transport
                .PullAsync(new PullRequest(checkpoint, _options.PullBatchSize), cancellationToken)
                .ConfigureAwait(false);
            ValidatePullPage(result, checkpoint);

            var updates = new List<RecordUpdate<TDocument>>(result.Changes.Count);
            foreach (var change in result.Changes)
            {
                // Keep the local clock ahead of every timestamp observed from other replicas.
                _clock.Update(change.Document.UpdatedAt);
                updates.Add(new RecordUpdate<TDocument>(change.Document.Id, existing => ApplyRemote(existing, change)));
            }

            var results = await _store.UpdateAsync(updates, result.Checkpoint, cancellationToken).ConfigureAwait(false);
            applied += results.Count(static r => r.Changed);
            checkpoint = result.Checkpoint;

            if (!result.HasMore)
            {
                return new SyncResult(applied, 0, 0);
            }
        }

        return new SyncResult(applied, 0, 0) { HasRemainingWork = true };
    }

    private void ValidatePullPage(PullResult<TDocument>? result, Checkpoint requested)
    {
        if (result?.Changes is null)
        {
            throw new SyncProtocolException("The server returned no pull result.");
        }

        if (result.Changes.Count > _options.PullBatchSize)
        {
            throw new SyncProtocolException("The server returned more changes than requested.");
        }

        if (result.HasMore && result.Checkpoint == requested)
        {
            throw new SyncProtocolException("The server reported more changes without advancing the checkpoint.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in result.Changes)
        {
            if (change?.Document is null || !SyncIds.IsValid(change.Document.Id) || change.Version < 1)
            {
                throw new SyncProtocolException("The server returned a malformed change.");
            }

            if (!ids.Add(change.Document.Id))
            {
                throw new SyncProtocolException($"The server returned document '{change.Document.Id}' twice in one page.");
            }
        }
    }

    private SyncRecord<TDocument>? ApplyRemote(SyncRecord<TDocument>? existing, RemoteChange<TDocument> change)
    {
        if (existing is null)
        {
            return new SyncRecord<TDocument>(_clone(change.Document), _clone(change.Document), IsDirty: false)
            {
                BaseVersion = change.Version,
            };
        }

        // Never regress to an older or equal server version (duplicate or reordered delivery).
        if (existing.KnownVersion is { } known && known >= change.Version)
        {
            return null;
        }

        // Unconfirmed local changes win locally until push resolves the divergence. The base stays the
        // state the edit was made against; the newer server state is remembered because the checkpoint
        // moves past it.
        if (existing.IsDirty)
        {
            return existing with { Observed = _clone(change.Document), ObservedVersion = change.Version };
        }

        return existing with
        {
            Current = _clone(change.Document),
            Base = _clone(change.Document),
            BaseVersion = change.Version,
            Observed = null,
            ObservedVersion = null,
        };
    }

    /// <summary>
    /// Drops an observed server state that the base has caught up with, and makes a clean record adopt an
    /// observed state that is newer than its base.
    /// </summary>
    private SyncRecord<TDocument> Settle(SyncRecord<TDocument> record)
    {
        if (record.ObservedVersion is not { } observed)
        {
            return record;
        }

        if (record.BaseVersion is { } baseVersion && observed <= baseVersion)
        {
            return record with { Observed = null, ObservedVersion = null };
        }

        return record.IsDirty
            ? record
            : record with
            {
                Current = _clone(record.Observed!),
                Base = _clone(record.Observed!),
                BaseVersion = observed,
                Observed = null,
                ObservedVersion = null,
            };
    }

    private async Task<SyncResult> PushCoreAsync(CancellationToken cancellationToken)
    {
        var pushed = 0;
        var conflicts = 0;
        var rejected = 0;
        var deferred = 0;
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var conflictCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var batch = 0; batch < _options.MaxPushBatches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await _store.GetPendingAsync(_options.PushBatchSize, excluded, cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                break;
            }

            var operations = await PrepareOperationsAsync(candidates, cancellationToken).ConfigureAwait(false);
            if (operations.Count == 0)
            {
                continue;
            }

            var response = await _transport
                .PushAsync(new PushRequest<TDocument>(operations), cancellationToken)
                .ConfigureAwait(false);
            var outcomes = CorrelateOutcomes(operations, response);

            var acknowledgements = new List<RecordUpdate<TDocument>>();
            foreach (var operation in operations)
            {
                if (!outcomes.TryGetValue(operation.OperationId, out var outcome))
                {
                    // Unknown result: keep the operation pending and resend the same id next time.
                    excluded.Add(operation.DocumentId);
                    deferred++;
                    continue;
                }

                switch (outcome.Kind)
                {
                    case PushOutcomeKind.Accepted:
                        _clock.Update(outcome.Document!.UpdatedAt);
                        acknowledgements.Add(new RecordUpdate<TDocument>(
                            operation.DocumentId,
                            existing => ApplyAccepted(existing, operation.OperationId, outcome)));
                        pushed++;
                        break;

                    case PushOutcomeKind.Rejected:
                        acknowledgements.Add(new RecordUpdate<TDocument>(
                            operation.DocumentId,
                            existing => ApplyRejected(existing, operation.OperationId, outcome)));
                        excluded.Add(operation.DocumentId);
                        rejected++;
                        break;

                    case PushOutcomeKind.RetryLater:
                        excluded.Add(operation.DocumentId);
                        deferred++;
                        break;
                }
            }

            await _store.UpdateAsync(acknowledgements, cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var operation in operations)
            {
                if (outcomes.TryGetValue(operation.OperationId, out var outcome) && outcome.Kind == PushOutcomeKind.Conflict)
                {
                    conflicts++;
                    var count = conflictCounts[operation.DocumentId] = conflictCounts.GetValueOrDefault(operation.DocumentId) + 1;
                    var stillPending = await ResolveConflictAsync(operation.OperationId, outcome, cancellationToken).ConfigureAwait(false);
                    if (stillPending && count >= _options.MaxConflictRetries)
                    {
                        excluded.Add(operation.DocumentId);
                        deferred++;
                    }
                }
            }
        }

        var remaining = await _store.GetPendingAsync(1, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new SyncResult(0, pushed, conflicts)
        {
            Rejected = rejected,
            Deferred = deferred,
            HasRemainingWork = remaining.Count > 0,
        };
    }

    /// <summary>
    /// Persists a new immutable operation for each candidate that does not already have one, then returns
    /// every candidate's pending operation. Existing pending operations are resent unchanged.
    /// </summary>
    private async Task<List<PushOperation<TDocument>>> PrepareOperationsAsync(
        IReadOnlyList<SyncRecord<TDocument>> candidates,
        CancellationToken cancellationToken)
    {
        var updates = new List<RecordUpdate<TDocument>>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var operationId = Guid.CreateVersion7().ToString("N");
            updates.Add(new RecordUpdate<TDocument>(candidate.Current.Id, existing =>
                existing is { IsPushable: true, Pending: null }
                    ? existing with
                    {
                        Pending = new PendingOperation<TDocument>(operationId, existing.LocalRevision, existing.BaseVersion, existing.Current),
                    }
                    : null));
        }

        var results = await _store.UpdateAsync(updates, cancellationToken: cancellationToken).ConfigureAwait(false);
        var operations = new List<PushOperation<TDocument>>(results.Count);
        foreach (var result in results)
        {
            if (result.Record is { IsPushable: true, Pending: { } pending } record)
            {
                operations.Add(new PushOperation<TDocument>(pending.OperationId, record.Current.Id, pending.BaseVersion, pending.Payload));
            }
        }

        return operations;
    }

    private static Dictionary<string, PushOutcome<TDocument>> CorrelateOutcomes(
        List<PushOperation<TDocument>> operations,
        PushResult<TDocument>? response)
    {
        if (response?.Outcomes is null)
        {
            throw new SyncProtocolException("The server returned no push result.");
        }

        var sent = operations.ToDictionary(static o => o.OperationId, StringComparer.Ordinal);
        var outcomes = new Dictionary<string, PushOutcome<TDocument>>(StringComparer.Ordinal);
        foreach (var outcome in response.Outcomes)
        {
            if (outcome is null || !sent.TryGetValue(outcome.OperationId, out var operation))
            {
                throw new SyncProtocolException($"The server reported an outcome for unknown operation '{outcome?.OperationId}'.");
            }

            if (!outcomes.TryAdd(outcome.OperationId, outcome))
            {
                throw new SyncProtocolException($"The server reported operation '{outcome.OperationId}' twice.");
            }

            var carriesState = outcome.Kind is PushOutcomeKind.Accepted or PushOutcomeKind.Conflict;
            if (carriesState
                && (outcome.Document is null
                    || outcome.Version is not >= 1
                    || !string.Equals(outcome.Document.Id, operation.DocumentId, StringComparison.Ordinal)))
            {
                throw new SyncProtocolException($"The server returned a malformed {outcome.Kind} outcome for '{outcome.OperationId}'.");
            }

            if (!Enum.IsDefined(outcome.Kind))
            {
                throw new SyncProtocolException($"The server returned an unknown outcome kind for '{outcome.OperationId}'.");
            }
        }

        return outcomes;
    }

    private SyncRecord<TDocument>? ApplyAccepted(SyncRecord<TDocument>? existing, string operationId, PushOutcome<TDocument> outcome)
    {
        // A stale or duplicate response for an operation that is no longer pending changes nothing.
        if (existing?.Pending is not { } pending || pending.OperationId != operationId)
        {
            return null;
        }

        var confirmed = outcome.Document!;
        var rebased = existing with { Base = _clone(confirmed), BaseVersion = outcome.Version, Pending = null };

        // Only the revision that was sent becomes clean; a later local edit stays dirty on the new base.
        return Settle(existing.LocalRevision == pending.Revision
            ? rebased with { Current = _clone(confirmed), IsDirty = false, Rejection = null }
            : rebased);
    }

    private static SyncRecord<TDocument>? ApplyRejected(SyncRecord<TDocument>? existing, string operationId, PushOutcome<TDocument> outcome)
    {
        if (existing?.Pending is not { } pending || pending.OperationId != operationId)
        {
            return null;
        }

        return existing.LocalRevision == pending.Revision
            ? existing with { Pending = null, Rejection = new SyncRejection(pending.Revision, outcome.ErrorCode ?? "rejected", outcome.Message) }
            : existing with { Pending = null };
    }

    /// <summary>Runs the conflict handler and commits its decision; returns whether the record is still pushable.</summary>
    private async Task<bool> ResolveConflictAsync(string operationId, PushOutcome<TDocument> outcome, CancellationToken cancellationToken)
    {
        var master = outcome.Document!;
        var masterVersion = outcome.Version!.Value;
        _clock.Update(master.UpdatedAt);

        var snapshot = await _store.GetAsync(master.Id, cancellationToken).ConfigureAwait(false);
        if (snapshot?.Pending?.OperationId != operationId)
        {
            return snapshot?.IsPushable ?? false;
        }

        var resolution = _conflictHandler.Resolve(new ConflictContext<TDocument>(
            RealMaster: _clone(master),
            AssumedMaster: snapshot.Base is { } b ? _clone(b) : null,
            Fork: _clone(snapshot.Current)));

        TDocument? resolved = null;
        if (resolution.Outcome == ConflictOutcome.UseResolved)
        {
            resolved = _clone(resolution.Resolved ?? throw new InvalidOperationException("A UseResolved resolution must carry a document."));
            resolved.Id = master.Id;
            resolved.UpdatedAt = _clock.Update(master.UpdatedAt);
        }

        var results = await _store.UpdateAsync(
            [new RecordUpdate<TDocument>(master.Id, existing =>
            {
                if (existing?.Pending?.OperationId != operationId)
                {
                    return null;
                }

                if (existing.LocalRevision != snapshot.LocalRevision)
                {
                    // Edited while resolving: drop the stale operation but keep the old base, so the
                    // next push conflicts again and the handler sees the latest local state.
                    return existing with { Pending = null };
                }

                var rebased = existing with { Base = _clone(master), BaseVersion = masterVersion, Pending = null };
                return Settle(resolution.Outcome switch
                {
                    ConflictOutcome.UseMaster => rebased with { Current = _clone(master), IsDirty = false, Rejection = null },
                    ConflictOutcome.KeepFork => rebased with { IsDirty = true },
                    _ => rebased with { Current = _clone(resolved!), IsDirty = true, LocalRevision = existing.LocalRevision + 1 },
                });
            })],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return results[0].Record?.IsPushable ?? false;
    }
}
