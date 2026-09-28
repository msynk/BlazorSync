using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorSync.Clocks;
using BlazorSync.Documents;
using BlazorSync.Protocol;

namespace BlazorSync.Server;

/// <summary>Configuration for <see cref="InMemorySyncServer{TDocument}"/>.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemorySyncServerOptions<TDocument>
    where TDocument : class, ISyncEntity
{
    /// <summary>Deep-clone function (for example <c>DocumentCloner.Json(context.MyDocument)</c>).</summary>
    public required Func<TDocument, TDocument> Cloner { get; init; }

    /// <summary>
    /// Produces a canonical string for a document, used to detect an operation id reused with a
    /// different payload (for example <c>DocumentCloner.JsonFingerprint(context.MyDocument)</c>).
    /// </summary>
    public required Func<TDocument, string> Fingerprint { get; init; }

    /// <summary>The physical clock used to validate origin timestamps. Defaults to the system clock.</summary>
    public IPhysicalClock? PhysicalClock { get; init; }

    /// <summary>
    /// How far a document's origin <see cref="ISyncEntity.UpdatedAt"/> may be ahead of server time before
    /// the write is rejected with <see cref="PushErrorCodes.ClockSkew"/>. Default five minutes.
    /// </summary>
    public TimeSpan MaxClockSkew { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum operations accepted in one push request. Default 1000.</summary>
    public int MaxOperationsPerPush { get; init; } = 1000;

    /// <summary>Maximum changes returned by one pull, regardless of the requested batch size. Default 1000.</summary>
    public int MaxPageSize { get; init; } = 1000;

    /// <summary>
    /// A backup to start from (see <see cref="InMemorySyncServer{TDocument}.CreateBackup"/>). The restored
    /// server gets a new epoch, so replicas holding checkpoints from the original must reset.
    /// </summary>
    public InMemorySyncServerBackup<TDocument>? RestoreFrom { get; init; }

    /// <summary>
    /// The lowest version the server may issue next is <c>VersionFloor + 1</c>. After a restore, set it at or
    /// above every version the lost history may have issued, so no version is ever reused for a different
    /// state (docs/protocol/v1.md, section 6).
    /// </summary>
    public long VersionFloor { get; init; }

    /// <summary>
    /// Optional fingerprint of what the caller may see (for example a hash of roles, grants or a filter). It is
    /// bound into every checkpoint; when it changes, the caller's old checkpoint yields
    /// <see cref="ResetReasons.ScopeChanged"/> and the replica resnapshots, which removes documents it may no longer
    /// read and brings back regranted ones even if they did not change.
    /// </summary>
    public Func<SyncCallContext, string>? ScopeFingerprint { get; init; }

    /// <summary>
    /// Optional read authorization. Documents the caller may not read are left out of its pull pages, and
    /// conflict or replayed outcomes that would reveal such a document are returned as
    /// <see cref="PushErrorCodes.Forbidden"/> rejections without the document.
    /// </summary>
    public Func<SyncCallContext, TDocument, bool>? CanRead { get; init; }

    /// <summary>
    /// Optional write authorization, checked before validation. Receives the operation and the current
    /// state (if any). A refused write is rejected with <see cref="PushErrorCodes.Forbidden"/>.
    /// </summary>
    public Func<SyncCallContext, PushOperation<TDocument>, TDocument?, bool>? CanWrite { get; init; }

    /// <summary>
    /// Optional application validation. Return <see langword="null"/> to allow the operation, or an error
    /// code to reject it permanently. Receives the operation and the current server state, if any.
    /// </summary>
    public Func<PushOperation<TDocument>, TDocument?, string?>? Validator { get; init; }
}

/// <summary>
/// A reference, in-memory implementation of the server side of the protocol. It is the authoritative
/// store: it assigns document versions from a single commit sequence, detects conflicts by comparing
/// each operation's base version with the current version, records the outcome of every operation so
/// that retries are answered without repeating the write, and serves the change feed by sequence.
/// </summary>
/// <remarks>
/// <para>
/// Because every operation executes under one lock, the commit sequence is also the visibility order,
/// so every issued checkpoint trivially covers a committed, gap-free prefix. A database-backed server
/// must prove the same property explicitly (see <c>docs/architecture/adr-005-feed-ordering.md</c>).
/// </para>
/// <para>
/// State, including operation receipts, lives only for the lifetime of the instance; receipts are never
/// expired. It performs no authentication; authorization is delegated to the optional
/// <see cref="InMemorySyncServerOptions{TDocument}.CanRead"/> and
/// <see cref="InMemorySyncServerOptions{TDocument}.CanWrite"/> hooks, and scopes are isolated by giving each
/// scope its own instance (<see cref="ScopedAuthority{TDocument}"/>). Intended for tests, samples and
/// in-process hosting only.
/// </para>
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemorySyncServer<TDocument> : ISyncAuthority<TDocument>, ISyncDocumentReader<TDocument>, ISyncCommitNotifier
    where TDocument : class, ISyncEntity
{
    private readonly Dictionary<string, Entry> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Receipt> _receipts = new(StringComparer.Ordinal);
    private readonly Func<TDocument, TDocument> _clone;
    private readonly Func<TDocument, string> _fingerprint;
    private readonly IPhysicalClock _physical;
    private readonly InMemorySyncServerOptions<TDocument> _options;
    private readonly object _gate = new();
    private long _sequence;
    private long _purgedThrough;

    /// <summary>
    /// Creates a server that clones and fingerprints documents with reflection-based JSON (not
    /// trim/AOT safe). Use the options constructor in trimmed or AOT-compiled apps.
    /// </summary>
    /// <param name="serverId">Prefix of the server's epoch identifier.</param>
    [RequiresUnreferencedCode("Uses reflection-based JSON. Use the options constructor for trimmed or AOT targets.")]
    [RequiresDynamicCode("Uses reflection-based JSON. Use the options constructor for trimmed or AOT targets.")]
    public InMemorySyncServer(string serverId = "server")
        : this(
            new InMemorySyncServerOptions<TDocument>
            {
                Cloner = static doc => DocumentCloner.JsonClone(doc),
                Fingerprint = static doc => JsonSerializer.Serialize(doc),
            },
            serverId)
    {
    }

    /// <summary>Creates a server from <paramref name="options"/>.</summary>
    /// <param name="options">The configuration.</param>
    /// <param name="serverId">Prefix of the server's epoch identifier.</param>
    public InMemorySyncServer(InMemorySyncServerOptions<TDocument> options, string serverId = "server")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Cloner, nameof(options.Cloner));
        ArgumentNullException.ThrowIfNull(options.Fingerprint, nameof(options.Fingerprint));
        ArgumentException.ThrowIfNullOrEmpty(serverId);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxOperationsPerPush, 1, nameof(options.MaxOperationsPerPush));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPageSize, 1, nameof(options.MaxPageSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxClockSkew, TimeSpan.Zero, nameof(options.MaxClockSkew));

        _options = options;
        _clone = options.Cloner;
        _fingerprint = options.Fingerprint;
        _physical = options.PhysicalClock ?? SystemPhysicalClock.Instance;
        ArgumentOutOfRangeException.ThrowIfNegative(options.VersionFloor, nameof(options.VersionFloor));
        _sequence = options.VersionFloor;
        if (options.RestoreFrom is { } backup)
        {
            foreach (var (id, entry) in backup.Documents)
            {
                _documents[id] = new Entry(_clone(entry.Document), entry.Version);
            }

            foreach (var (id, receipt) in backup.Receipts)
            {
                _receipts[id] = new Receipt(receipt.Fingerprint, receipt.Outcome with { Document = receipt.Outcome.Document is { } d ? _clone(d) : null });
            }

            _sequence = Math.Max(_sequence, backup.Sequence);
            _purgedThrough = backup.PurgedThrough;
        }

        Epoch = $"{serverId}-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Copies the documents, operation receipts and version sequence, as a database backup would.
    /// Starting a server from it (<see cref="InMemorySyncServerOptions{TDocument}.RestoreFrom"/>) simulates a
    /// restore that loses everything committed after this point.
    /// </summary>
    public InMemorySyncServerBackup<TDocument> CreateBackup()
    {
        lock (_gate)
        {
            return new InMemorySyncServerBackup<TDocument>(
                _documents.ToDictionary(kv => kv.Key, kv => (_clone(kv.Value.Document), kv.Value.Version), StringComparer.Ordinal),
                _receipts.ToDictionary(kv => kv.Key, kv => (kv.Value.Fingerprint, kv.Value.Outcome with { Document = kv.Value.Outcome.Document is { } d ? _clone(d) : null }), StringComparer.Ordinal),
                _sequence,
                _purgedThrough);
        }
    }

    /// <summary>The highest version issued so far (diagnostics).</summary>
    public long HighestVersion
    {
        get
        {
            lock (_gate)
            {
                return _sequence;
            }
        }
    }

    /// <summary>
    /// Identifies this server's feed history. Checkpoints from another epoch cannot be resumed.
    /// </summary>
    public string Epoch { get; }

    /// <summary>The number of stored operation receipts (diagnostics).</summary>
    public int ReceiptCount
    {
        get
        {
            lock (_gate)
            {
                return _receipts.Count;
            }
        }
    }

    /// <inheritdoc />
    public AuthorityLimits Limits => new(_options.MaxOperationsPerPush, _options.MaxPageSize);

    /// <inheritdoc />
    public Task<PullResult<TDocument>> PullAsync(SyncCallContext context, PullRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Pull(context, request));
    }

    /// <inheritdoc />
    public Task<PushResult<TDocument>> PushAsync(SyncCallContext context, PushRequest<TDocument> request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Push(context, request));
    }

    /// <summary>Serves the next page of changes for an anonymous caller.</summary>
    public PullResult<TDocument> Pull(PullRequest request) => Pull(SyncCallContext.Anonymous, request);

    /// <summary>Decides a push for an anonymous caller.</summary>
    public PushResult<TDocument> Push(PushRequest<TDocument> request) => Push(SyncCallContext.Anonymous, request);

    /// <summary>Serves the next page of changes strictly after <paramref name="request"/>'s checkpoint.</summary>
    /// <exception cref="SyncResetRequiredException">The checkpoint belongs to a different epoch.</exception>
    /// <exception cref="SyncProtocolException">The checkpoint or batch size is malformed.</exception>
    public PullResult<TDocument> Pull(SyncCallContext context, PullRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (request.BatchSize < 1)
        {
            throw new SyncProtocolException("The pull limit must be at least 1.");
        }

        var since = ParseCheckpoint(context, request.Since);
        var limit = Math.Min(request.BatchSize, _options.MaxPageSize);

        lock (_gate)
        {
            // Tombstones at or below the horizon are gone: a replica that has not seen everything up to it cannot
            // learn about those deletions incrementally and must resnapshot.
            if (since > 0 && since < _purgedThrough)
            {
                throw new SyncResetRequiredException("The checkpoint is older than the retention horizon.", ResetReasons.Expired);
            }

            var candidates = _documents.Values
                .Where(e => e.Version > since)
                .OrderBy(static e => e.Version)
                .Take(limit + 1)
                .ToList();

            var hasMore = candidates.Count > limit;
            var window = candidates.Take(limit).ToList();

            // Unreadable documents are skipped, but the checkpoint still moves past them.
            var page = window
                .Where(e => _options.CanRead?.Invoke(context, e.Document) ?? true)
                .Select(e => new RemoteChange<TDocument>(_clone(e.Document), e.Version))
                .ToList();

            var position = window.Count > 0 ? window[^1].Version : Math.Max(since, 0);
            return new PullResult<TDocument>(page, FormatCheckpoint(context, position), hasMore) { Features = [SyncFeatures.Groups] };
        }
    }

    /// <summary>
    /// Applies a batch of independent operations and returns exactly one outcome per operation, in
    /// request order.
    /// </summary>
    /// <exception cref="SyncTransportException">The request exceeds <see cref="InMemorySyncServerOptions{TDocument}.MaxOperationsPerPush"/> (<c>payload-too-large</c>).</exception>
    public PushResult<TDocument> Push(SyncCallContext context, PushRequest<TDocument> request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operations is null)
        {
            throw new SyncProtocolException("The push request has no operations.");
        }

        if (request.Operations.Count > _options.MaxOperationsPerPush)
        {
            throw new SyncTransportException(
                SyncErrorCodes.PayloadTooLarge,
                $"A push may carry at most {_options.MaxOperationsPerPush} operations.",
                isTransient: false);
        }

        var outcomes = new PushOutcome<TDocument>[request.Operations.Count];
        var documentsInRequest = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < request.Operations.Count; i++)
            {
                var operation = request.Operations[i];
                if (operation?.Group is { } group)
                {
                    (groups.TryGetValue(group, out var members) ? members : groups[group] = []).Add(i);
                    continue;
                }

                outcomes[i] = Visible(context, Apply(context, operation, documentsInRequest));
            }

            foreach (var (group, members) in groups)
            {
                ApplyGroup(context, group, members, request.Operations, outcomes, documentsInRequest);
            }
        }

        var committed = request.Operations
            .Zip(outcomes)
            .Where(pair => pair.Second is { Kind: PushOutcomeKind.Accepted, IsDuplicate: false })
            .Select(pair => pair.First.DocumentId)
            .ToList();
        if (committed.Count > 0)
        {
            // Outside the lock: handlers must never delay or block writers.
            Committed?.Invoke(new AuthorityCommit(context.Scope, committed));
        }

        return new PushResult<TDocument>(outcomes);
    }

    /// <inheritdoc />
    public event Action<AuthorityCommit>? Committed;

    /// <inheritdoc />
    public Task<StoredDocument<TDocument>?> GetAsync(SyncCallContext context, string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(id);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _documents.TryGetValue(id, out var entry) && (_options.CanRead?.Invoke(context, entry.Document) ?? true)
                    ? new StoredDocument<TDocument>(_clone(entry.Document), entry.Version)
                    : null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredDocument<TDocument>>> ListAsync(SyncCallContext context, int limit, string? afterId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<StoredDocument<TDocument>> page = _documents
                .Where(kv => afterId is null || string.CompareOrdinal(kv.Key, afterId) > 0)
                .Where(kv => !kv.Value.Document.Deleted && (_options.CanRead?.Invoke(context, kv.Value.Document) ?? true))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(Math.Min(limit, _options.MaxPageSize))
                .Select(kv => new StoredDocument<TDocument>(_clone(kv.Value.Document), kv.Value.Version))
                .ToList();
            return Task.FromResult(page);
        }
    }

    /// <summary>Returns a snapshot of the current documents (test/diagnostic helper).</summary>
    public IReadOnlyList<TDocument> Snapshot(bool includeDeleted = true)
    {
        lock (_gate)
        {
            return _documents.Values
                .Where(e => includeDeleted || !e.Document.Deleted)
                .Select(e => _clone(e.Document))
                .ToList();
        }
    }

    /// <summary>Returns the current version of <paramref name="id"/>, or <see langword="null"/> if unknown.</summary>
    public long? GetVersion(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            return _documents.TryGetValue(id, out var entry) ? entry.Version : null;
        }
    }

    /// <summary>Never reveals a document the caller may not read, whether the outcome is fresh or replayed.</summary>
    private PushOutcome<TDocument> Visible(SyncCallContext context, PushOutcome<TDocument> outcome) =>
        outcome.Document is { } document && _options.CanRead is { } canRead && !canRead(context, document)
            ? PushOutcome<TDocument>.Rejected(outcome.OperationId, PushErrorCodes.Forbidden) with { IsDuplicate = outcome.IsDuplicate }
            : outcome;

    private PushOutcome<TDocument> Apply(SyncCallContext context, PushOperation<TDocument>? operation, HashSet<string> documentsInRequest)
    {
        if (Validate(operation, documentsInRequest) is { } invalid)
        {
            return invalid;
        }

        if (Replay(operation!) is { } replayed)
        {
            return replayed;
        }

        var outcome = Evaluate(context, operation!) ?? Commit(operation!);
        Remember(operation!, outcome);
        return outcome;
    }

    /// <summary>
    /// A dependency group (protocol §4.1): every member is decided, and the accepted ones are committed only if all of
    /// them would be accepted. Otherwise the members that failed keep their outcome and the others get
    /// <see cref="PushOutcomeKind.RetryLater"/> with <see cref="PushErrorCodes.GroupAborted"/> and no receipt.
    /// </summary>
    private void ApplyGroup(SyncCallContext context, string group, List<int> members, IReadOnlyList<PushOperation<TDocument>> operations, PushOutcome<TDocument>[] outcomes, HashSet<string> documentsInRequest)
    {
        var invalid = !SyncIds.IsValid(group) || members.Any(i => operations[i].GroupSize != members.Count);
        foreach (var i in members)
        {
            if (Validate(operations[i], documentsInRequest) is { } rejected)
            {
                outcomes[i] = rejected;
                invalid = true;
            }
        }

        if (invalid)
        {
            foreach (var i in members)
            {
                outcomes[i] = outcomes[i] ?? PushOutcome<TDocument>.Rejected(operations[i].OperationId, PushErrorCodes.Invalid, "The dependency group is malformed or incomplete.");
            }

            return;
        }

        // Evaluate everything first; nothing changes until the whole group is known to succeed.
        var decided = new PushOutcome<TDocument>?[members.Count];
        var allAccept = true;
        for (var m = 0; m < members.Count; m++)
        {
            var operation = operations[members[m]];
            decided[m] = Replay(operation) ?? Evaluate(context, operation);
            allAccept &= decided[m] is null or { Kind: PushOutcomeKind.Accepted };
        }

        for (var m = 0; m < members.Count; m++)
        {
            var operation = operations[members[m]];
            PushOutcome<TDocument> outcome;
            if (decided[m] is { } final)
            {
                outcome = final;
                if (!final.IsDuplicate)
                {
                    Remember(operation, final);
                }
            }
            else if (allAccept)
            {
                outcome = Commit(operation);
                Remember(operation, outcome);
            }
            else
            {
                outcome = PushOutcome<TDocument>.RetryLater(operation.OperationId, PushErrorCodes.GroupAborted, "Another change of the same group was not accepted.");
            }

            outcomes[members[m]] = Visible(context, outcome);
        }
    }

    private static PushOutcome<TDocument>? Validate(PushOperation<TDocument>? operation, HashSet<string> documentsInRequest)
    {
        if (operation is null || !SyncIds.IsValid(operation.OperationId))
        {
            return PushOutcome<TDocument>.Rejected(operation?.OperationId ?? string.Empty, PushErrorCodes.Invalid, "Missing or invalid operation id.");
        }

        if (!SyncIds.IsValid(operation.DocumentId)
            || operation.Document is null
            || !string.Equals(operation.Document.Id, operation.DocumentId, StringComparison.Ordinal)
            || operation.BaseVersion is < 1)
        {
            return PushOutcome<TDocument>.Rejected(operation.OperationId, PushErrorCodes.Invalid, "Malformed operation.");
        }

        return documentsInRequest.Add(operation.DocumentId)
            ? null
            : PushOutcome<TDocument>.Rejected(operation.OperationId, PushErrorCodes.Invalid, "A push may contain one operation per document.");
    }

    private PushOutcome<TDocument>? Replay(PushOperation<TDocument> operation) =>
        _receipts.TryGetValue(operation.OperationId, out var receipt)
            ? receipt.Fingerprint == Fingerprint(operation)
                ? receipt.Outcome with { IsDuplicate = true, Document = receipt.Outcome.Document is { } d ? _clone(d) : null }
                : PushOutcome<TDocument>.Rejected(operation.OperationId, PushErrorCodes.OperationIdReused, "The operation id was already used for a different request.")
            : null;

    private void Remember(PushOperation<TDocument> operation, PushOutcome<TDocument> outcome) =>
        _receipts[operation.OperationId] = new Receipt(Fingerprint(operation), outcome with { Document = outcome.Document is { } doc ? _clone(doc) : null });

    /// <summary>Decides an operation without changing anything: a final outcome, or <see langword="null"/> if it would be accepted.</summary>
    private PushOutcome<TDocument>? Evaluate(SyncCallContext context, PushOperation<TDocument> operation)
    {
        var opId = operation.OperationId;
        var id = operation.DocumentId;
        var limit = _physical.NowMilliseconds() + (long)_options.MaxClockSkew.TotalMilliseconds;
        if (operation.Document.UpdatedAt.WallTime > limit)
        {
            return PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.ClockSkew, "The document's timestamp is too far in the future.");
        }

        _documents.TryGetValue(id, out var current);

        // Without the document and with a base at or below the horizon, it may have been deleted and purged:
        // accepting would resurrect it. A base above the horizon (a restore lost it) is still accepted.
        if (current is null && operation.BaseVersion is { } baseVersion && baseVersion <= _purgedThrough)
        {
            return PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.BaseExpired, "The document no longer exists on the server.");
        }

        if (_options.CanWrite is { } canWrite && !canWrite(context, operation, current is null ? null : _clone(current.Document)))
        {
            return PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.Forbidden);
        }

        if (_options.Validator?.Invoke(operation, current is null ? null : _clone(current.Document)) is { } error)
        {
            return PushOutcome<TDocument>.Rejected(opId, error);
        }

        // Accept only if the base names the current version. A missing current state accepts any base:
        // there is nothing to overwrite. A null base against an existing document is a same-id insert.
        if (current is not null && operation.BaseVersion != current.Version)
        {
            return PushOutcome<TDocument>.Conflict(opId, current.Version, _clone(current.Document));
        }

        return null;
    }

    private PushOutcome<TDocument> Commit(PushOperation<TDocument> operation)
    {
        var version = ++_sequence;
        var stored = _clone(operation.Document);
        _documents[operation.DocumentId] = new Entry(stored, version);
        return PushOutcome<TDocument>.Accepted(operation.OperationId, version, _clone(stored));
    }

    private string Fingerprint(PushOperation<TDocument> operation)
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{operation.DocumentId.Length}:{operation.DocumentId}|{operation.BaseVersion}|{operation.Group}|{operation.GroupSize}|{_fingerprint(operation.Document)}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// Removes tombstones with a version at or below <paramref name="throughVersion"/> and raises the retention
    /// horizon. Afterwards, checkpoints below the horizon get <see cref="ResetReasons.Expired"/>, and a write based on
    /// a version at or below it for a document that no longer exists is rejected with
    /// <see cref="PushErrorCodes.BaseExpired"/>, so a long-offline replica cannot resurrect a purged document.
    /// </summary>
    /// <returns>The number of tombstones removed.</returns>
    public int PurgeTombstones(long throughVersion)
    {
        lock (_gate)
        {
            throughVersion = Math.Min(throughVersion, _sequence);
            var expired = _documents.Where(kv => kv.Value.Document.Deleted && kv.Value.Version <= throughVersion).Select(kv => kv.Key).ToList();
            foreach (var id in expired)
            {
                _documents.Remove(id);
            }

            _purgedThrough = Math.Max(_purgedThrough, throughVersion);
            return expired.Count;
        }
    }

    /// <summary>Removes receipts of operations accepted at or below <paramref name="throughVersion"/>.</summary>
    /// <remarks>
    /// Keep receipts longer than any replica may stay offline. A replay after its receipt is gone can never be applied
    /// twice (the accepted write changed the version, so the replay's base no longer matches), but it is answered as a
    /// conflict, so the replica's conflict handler runs for a write that had in fact succeeded.
    /// </remarks>
    /// <returns>The number of receipts removed.</returns>
    public int PurgeReceipts(long throughVersion)
    {
        lock (_gate)
        {
            var expired = _receipts
                .Where(kv => kv.Value.Outcome.Kind == PushOutcomeKind.Accepted && kv.Value.Outcome.Version <= throughVersion)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var opId in expired)
            {
                _receipts.Remove(opId);
            }

            return expired.Count;
        }
    }

    /// <summary>The retention horizon: the highest version whose tombstones may have been purged.</summary>
    public long PurgedThrough
    {
        get
        {
            lock (_gate)
            {
                return _purgedThrough;
            }
        }
    }

    // Checkpoint: "{epoch}~{scope}:{position}", where scope is a short hash of the caller's scope fingerprint.
    private Checkpoint FormatCheckpoint(SyncCallContext context, long position) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{Epoch}~{ScopeHash(context)}:{position}"));

    private string ScopeHash(SyncCallContext context) =>
        _options.ScopeFingerprint is { } fingerprint
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint(context))))[..16]
            : string.Empty;

    private long ParseCheckpoint(SyncCallContext context, Checkpoint checkpoint)
    {
        if (checkpoint.IsStart)
        {
            return 0;
        }

        var value = checkpoint.Value!;
        var separator = value.LastIndexOf(':');
        var scopeSeparator = value.LastIndexOf('~', Math.Max(separator, 0));
        if (separator <= 0
            || !long.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            throw new SyncProtocolException($"Malformed checkpoint '{value}'.");
        }

        if (scopeSeparator <= 0)
        {
            // "{epoch}:{position}", issued before checkpoints carried a scope: resnapshot rather than strand the replica.
            throw new SyncResetRequiredException("The checkpoint was issued by an older server version.", ResetReasons.Epoch);
        }

        if (!value.AsSpan(0, scopeSeparator).SequenceEqual(Epoch))
        {
            throw new SyncResetRequiredException("The checkpoint was issued by a different server epoch.", ResetReasons.Epoch);
        }

        if (!value.AsSpan(scopeSeparator + 1, separator - scopeSeparator - 1).SequenceEqual(ScopeHash(context)))
        {
            throw new SyncResetRequiredException("What this caller may see has changed since the checkpoint was issued.", ResetReasons.ScopeChanged);
        }

        return position;
    }

    private sealed record Entry(TDocument Document, long Version);

    private sealed record Receipt(string Fingerprint, PushOutcome<TDocument> Outcome);
}

/// <summary>A point-in-time copy of an <see cref="InMemorySyncServer{TDocument}"/>'s state.</summary>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemorySyncServerBackup<TDocument>
    where TDocument : class, ISyncEntity
{
    internal InMemorySyncServerBackup(
        IReadOnlyDictionary<string, (TDocument Document, long Version)> documents,
        IReadOnlyDictionary<string, (string Fingerprint, PushOutcome<TDocument> Outcome)> receipts,
        long sequence,
        long purgedThrough)
    {
        Documents = documents;
        Receipts = receipts;
        Sequence = sequence;
        PurgedThrough = purgedThrough;
    }

    internal long PurgedThrough { get; }

    internal IReadOnlyDictionary<string, (TDocument Document, long Version)> Documents { get; }

    internal IReadOnlyDictionary<string, (string Fingerprint, PushOutcome<TDocument> Outcome)> Receipts { get; }

    /// <summary>The highest version issued when the backup was taken.</summary>
    public long Sequence { get; }
}
