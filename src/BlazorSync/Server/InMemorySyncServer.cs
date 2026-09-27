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
/// expired. It performs no authentication or authorization and is intended for tests, samples and
/// in-process hosting only.
/// </para>
/// </remarks>
/// <typeparam name="TDocument">The synchronized entity type.</typeparam>
public sealed class InMemorySyncServer<TDocument>
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
        Epoch = $"{serverId}-{Guid.NewGuid():N}";
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

    /// <summary>Serves the next page of changes strictly after <paramref name="request"/>'s checkpoint.</summary>
    /// <exception cref="SyncResetRequiredException">The checkpoint belongs to a different epoch.</exception>
    /// <exception cref="SyncProtocolException">The checkpoint is malformed.</exception>
    public PullResult<TDocument> Pull(PullRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.BatchSize, 1, nameof(request.BatchSize));
        var since = ParseCheckpoint(request.Since);
        var limit = Math.Min(request.BatchSize, _options.MaxPageSize);

        lock (_gate)
        {
            var candidates = _documents.Values
                .Where(e => e.Version > since)
                .OrderBy(static e => e.Version)
                .Take(limit + 1)
                .ToList();

            var hasMore = candidates.Count > limit;
            var page = candidates
                .Take(limit)
                .Select(e => new RemoteChange<TDocument>(_clone(e.Document), e.Version))
                .ToList();

            var position = page.Count > 0 ? page[^1].Version : Math.Max(since, 0);
            return new PullResult<TDocument>(page, FormatCheckpoint(position), hasMore);
        }
    }

    /// <summary>
    /// Applies a batch of independent operations and returns exactly one outcome per operation, in
    /// request order.
    /// </summary>
    /// <exception cref="ArgumentException">The request exceeds <see cref="InMemorySyncServerOptions{TDocument}.MaxOperationsPerPush"/>.</exception>
    public PushResult<TDocument> Push(PushRequest<TDocument> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Operations);
        if (request.Operations.Count > _options.MaxOperationsPerPush)
        {
            throw new ArgumentException(
                $"A push may carry at most {_options.MaxOperationsPerPush} operations.", nameof(request));
        }

        var outcomes = new List<PushOutcome<TDocument>>(request.Operations.Count);
        var documentsInRequest = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var operation in request.Operations)
            {
                outcomes.Add(Apply(operation, documentsInRequest));
            }
        }

        return new PushResult<TDocument>(outcomes);
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

    private PushOutcome<TDocument> Apply(PushOperation<TDocument>? operation, HashSet<string> documentsInRequest)
    {
        if (operation is null || !SyncIds.IsValid(operation.OperationId))
        {
            return PushOutcome<TDocument>.Rejected(operation?.OperationId ?? string.Empty, PushErrorCodes.Invalid, "Missing or invalid operation id.");
        }

        var opId = operation.OperationId;
        if (!SyncIds.IsValid(operation.DocumentId)
            || operation.Document is null
            || !string.Equals(operation.Document.Id, operation.DocumentId, StringComparison.Ordinal)
            || operation.BaseVersion is < 1)
        {
            return PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.Invalid, "Malformed operation.");
        }

        if (!documentsInRequest.Add(operation.DocumentId))
        {
            return PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.Invalid, "A push may contain one operation per document.");
        }

        var fingerprint = Fingerprint(operation);
        if (_receipts.TryGetValue(opId, out var receipt))
        {
            return receipt.Fingerprint == fingerprint
                ? receipt.Outcome with { IsDuplicate = true, Document = receipt.Outcome.Document is { } d ? _clone(d) : null }
                : PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.OperationIdReused, "The operation id was already used for a different request.");
        }

        var outcome = Decide(operation);
        _receipts[opId] = new Receipt(fingerprint, outcome with { Document = outcome.Document is { } doc ? _clone(doc) : null });
        return outcome;
    }

    private PushOutcome<TDocument> Decide(PushOperation<TDocument> operation)
    {
        var opId = operation.OperationId;
        var id = operation.DocumentId;
        var limit = _physical.NowMilliseconds() + (long)_options.MaxClockSkew.TotalMilliseconds;
        if (operation.Document.UpdatedAt.WallTime > limit)
        {
            return PushOutcome<TDocument>.Rejected(opId, PushErrorCodes.ClockSkew, "The document's timestamp is too far in the future.");
        }

        _documents.TryGetValue(id, out var current);

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

        var version = ++_sequence;
        var stored = _clone(operation.Document);
        _documents[id] = new Entry(stored, version);
        return PushOutcome<TDocument>.Accepted(opId, version, _clone(stored));
    }

    private string Fingerprint(PushOperation<TDocument> operation)
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{operation.DocumentId.Length}:{operation.DocumentId}|{operation.BaseVersion}|{_fingerprint(operation.Document)}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private Checkpoint FormatCheckpoint(long position) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{Epoch}:{position}"));

    private long ParseCheckpoint(Checkpoint checkpoint)
    {
        if (checkpoint.IsStart)
        {
            return 0;
        }

        var value = checkpoint.Value!;
        var separator = value.LastIndexOf(':');
        if (separator <= 0
            || !long.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            throw new SyncProtocolException($"Malformed checkpoint '{value}'.");
        }

        if (!value.AsSpan(0, separator).SequenceEqual(Epoch))
        {
            throw new SyncResetRequiredException("The checkpoint was issued by a different server epoch.");
        }

        return position;
    }

    private sealed record Entry(TDocument Document, long Version);

    private sealed record Receipt(string Fingerprint, PushOutcome<TDocument> Outcome);
}
